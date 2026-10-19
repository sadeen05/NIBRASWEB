using Microsoft.Extensions.Hosting;

namespace NIBRAS.API.Services;

/// <summary>
/// The managed worker that delivers the verification mails. It reads the bounded queue and
/// performs one delivery at a time, each in its own scope, so the amount of work, the number
/// of database contexts and the number of mail hand-offs are all limited.
/// <para>
/// The worker is **not** a durable queue: a request that was accepted but not delivered
/// before the host stopped is lost, and a crash loses everything that was still queued. The
/// loss is counted and logged, but it cannot always be audited - the audit trail only records
/// what the delivery service itself recorded. A recipient that receives no mail asks for
/// another one, and the per-account cooldown and daily cap keep that bounded.
/// </para>
/// </summary>
public sealed class VerificationDeliveryWorker : BackgroundService
{
    /// <summary>
    /// The time the accepted work may take to finish when the host stops. It is shorter than
    /// the default shutdown timeout of the host, so the worker can report what it could not
    /// finish before the process goes away.
    /// </summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The time a single delivery may take. It is above the timeout of the mail client, so a
    /// slow mail server is reported as a failed delivery instead of stalling the worker.
    /// </summary>
    private static readonly TimeSpan ItemTimeout = TimeSpan.FromSeconds(60);

    private readonly IVerificationDeliveryQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VerificationDeliveryCounters _counters;
    private readonly ILogger<VerificationDeliveryWorker> _logger;
    private readonly CancellationTokenSource _drainCts = new();
    private int _workerFinished;

    public VerificationDeliveryWorker(
        IVerificationDeliveryQueue queue,
        IServiceScopeFactory scopeFactory,
        VerificationDeliveryCounters counters,
        ILogger<VerificationDeliveryWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _counters = counters;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The reads use the drain token and not the host's stopping token: the host cancels
            // that one as soon as the shutdown starts, which would abandon the work that was
            // already accepted. The drain token is cancelled when the drain period is over.
            await foreach (var request in _queue.ReadAllAsync(_drainCts.Token))
            {
                await ProcessAsync(request);
            }
        }
        catch (OperationCanceledException)
        {
            // The drain period is over. The requests that are still queued are counted as
            // interrupted when the worker stops.
        }
        catch (Exception ex)
        {
            // A failure that is not one delivery is fatal for the worker. The host is stopped
            // (BackgroundServiceExceptionBehavior.StopHost) instead of silently accepting
            // requests that can never be delivered: a worker that is gone but a host that
            // still answers 202 would be a silent degradation. The reason is logged by its
            // type, because a database error can carry the address of the account. The
            // application never restarts the worker itself; the deployment does.
            _logger.LogError(
                "The verification delivery worker failed ({ErrorType}); the host is stopping.",
                ex.GetType().Name);
            throw;
        }
        finally
        {
            // The worker has ended: the drain token is not used any more and can be disposed.
            Interlocked.Exchange(ref _workerFinished, 1);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // 1. The queue stops accepting new requests.
        _queue.Complete();

        // 2. The accepted work gets a bounded period to finish.
        _drainCts.CancelAfter(DrainTimeout);

        try
        {
            await base.StopAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host cut the shutdown short.
        }
        catch (Exception ex)
        {
            // The worker ended with a fatal failure. It was already logged where it happened;
            // the shutdown continues, so the work that is left is counted and the summary is
            // still written.
            _logger.LogError(
                "The verification delivery worker ended with a failure ({ErrorType}).",
                ex.GetType().Name);
        }

        // 3. Whatever is left was accepted but not processed.
        var interrupted = _queue.DrainRemaining();
        if (interrupted > 0)
        {
            _counters.MarkInterrupted(interrupted);
        }

        // 4. The drain token is disposed when the worker has really ended. When the host cut
        // the shutdown short the worker may still be running, and a token that is disposed
        // under it would only produce another error.
        if (Volatile.Read(ref _workerFinished) == 1)
        {
            _drainCts.Dispose();
        }

        // 5. The summary. It carries counts only, never an address and never a token. The
        // accepted requests are accounted for: every one of them is either processed, failed
        // or interrupted.
        _logger.LogInformation(
            "The verification delivery worker stopped: accepted {Accepted}, refused {Refused}, processed {Processed}, failed {Failed}, interrupted {Interrupted}, pending {Pending}.",
            _counters.Accepted,
            _counters.Rejected,
            _counters.Processed,
            _counters.Failed,
            _counters.Interrupted,
            _queue.Pending);
    }

    /// <summary>
    /// Performs one delivery in its own scope. The verification token is created here, inside
    /// the worker's scope, so it never rests in the queue and never leaves the process.
    /// </summary>
    private async Task ProcessAsync(VerificationDeliveryRequest request)
    {
        using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(_drainCts.Token);
        itemCts.CancelAfter(ItemTimeout);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRegistrationVerificationService>();
            await service.SendToAddressAsync(request.Email, request.Ip, itemCts.Token);
            _counters.MarkProcessed();
        }
        catch (OperationCanceledException)
        {
            // The item was interrupted: the drain period ended, or the host is stopping. The
            // delivery may or may not have reached the mailer; it is not audited, because the
            // delivery service did not get to write the outcome.
            _counters.MarkInterrupted();
            _logger.LogWarning(
                "A verification delivery was interrupted. Interrupted: {Interrupted}.",
                _counters.Interrupted);
        }
        catch (Exception ex)
        {
            _counters.MarkFailed();

            // The exception is reported by its type only: a database error can carry the
            // address of the account, and the log must not contain it.
            _logger.LogError(
                "A verification delivery failed ({ErrorType}). Failed: {Failed}.",
                ex.GetType().Name,
                _counters.Failed);
        }
    }
}
