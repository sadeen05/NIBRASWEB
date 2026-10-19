using System.Threading.Channels;

namespace NIBRAS.API.Services;

/// <summary>
/// One request for a verification mail. It carries the minimum: the address of the account
/// and the remote address of the request. The verification token is deliberately **not** part
/// of it - it is created inside the worker, in the worker's own scope, so no secret ever
/// rests in the queue and no secret is ever handed over between two components.
/// </summary>
/// <param name="Email">The address the account was created with.</param>
/// <param name="Ip">The remote address of the request, for the audit trail. Never a credential.</param>
public sealed record VerificationDeliveryRequest(string Email, string? Ip);

/// <summary>
/// The counters of the verification delivery. They are plain counts: no address, no token and
/// no other secret ever reaches them, so they can be logged and monitored as they are.
/// </summary>
public sealed class VerificationDeliveryCounters
{
    private long _accepted;
    private long _rejected;
    private long _processed;
    private long _failed;
    private long _interrupted;

    /// <summary>Requests that were accepted into the queue.</summary>
    public long Accepted => Interlocked.Read(ref _accepted);

    /// <summary>Requests that were refused because the queue was full or shutting down.</summary>
    public long Rejected => Interlocked.Read(ref _rejected);

    /// <summary>Accepted requests that were processed without an error.</summary>
    /// <remarks>
    /// "Processed" does **not** mean that a mail was sent: a request for an address that
    /// belongs to no account, or to an account that no longer needs a verification, is
    /// processed as well and produces no mail. Whether a mail was handed over is recorded in
    /// the audit trail, not here.
    /// </remarks>
    public long Processed => Interlocked.Read(ref _processed);

    /// <summary>Accepted requests whose processing ended with an error.</summary>
    public long Failed => Interlocked.Read(ref _failed);

    /// <summary>Accepted requests that were not finished - the drain period ended.</summary>
    public long Interrupted => Interlocked.Read(ref _interrupted);

    public void MarkAccepted() => Interlocked.Increment(ref _accepted);

    public void MarkRejected() => Interlocked.Increment(ref _rejected);

    public void MarkProcessed() => Interlocked.Increment(ref _processed);

    public void MarkFailed() => Interlocked.Increment(ref _failed);

    public void MarkInterrupted(int count = 1) => Interlocked.Add(ref _interrupted, count);
}

/// <summary>
/// The queue of the verification deliveries. It is bounded, so the amount of pending work is
/// limited, and it never makes a caller wait: a request that does not fit is refused instead
/// of blocking the request that offered it.
/// </summary>
public interface IVerificationDeliveryQueue
{
    /// <summary>
    /// Accepts a request for a later delivery.
    /// </summary>
    /// <param name="request">The request; it carries no secret.</param>
    /// <returns>
    /// True when the request was accepted, false when the queue is full or is shutting down.
    /// The caller must not wait for capacity and must not report the refusal to an anonymous
    /// caller: the answer of the endpoint is the same in every case.
    /// </returns>
    bool TryEnqueue(VerificationDeliveryRequest request);

    /// <summary>Stops the queue from accepting further requests.</summary>
    void Complete();

    /// <summary>Reads the accepted requests until the queue is completed and empty.</summary>
    IAsyncEnumerable<VerificationDeliveryRequest> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes the requests that are still queued and returns how many there were. It is used
    /// when the worker stops, so the work that was accepted but not finished is counted.
    /// </summary>
    int DrainRemaining();

    /// <summary>
    /// The number of requests that are waiting at the moment. It is a snapshot: the worker
    /// takes the requests away while it is asked.
    /// </summary>
    int Pending { get; }
}

/// <summary>
/// The bounded queue of the verification deliveries. The capacity limits the amount of pending
/// work, and the refusal of a full queue is detected and counted instead of being hidden.
/// </summary>
public sealed class VerificationDeliveryQueue : IVerificationDeliveryQueue
{
    /// <summary>
    /// The number of requests that may wait at the same time. A hundred pending deliveries
    /// are far more than a normal burst produces, and they bound the work the worker has to
    /// carry over into a restart.
    /// </summary>
    public const int Capacity = 100;

    /// <summary>
    /// How often a refused request is written to the log. The individual refusals are counted
    /// all the time, but a flood of them must not flood the log as well.
    /// </summary>
    private const int RefusalLogInterval = 50;

    private readonly Channel<VerificationDeliveryRequest> _channel;
    private readonly VerificationDeliveryCounters _counters;
    private readonly ILogger<VerificationDeliveryQueue> _logger;

    public VerificationDeliveryQueue(
        VerificationDeliveryCounters counters,
        ILogger<VerificationDeliveryQueue> logger)
    {
        _counters = counters;
        _logger = logger;

        // FullMode.Wait makes the channel report a full queue to TryWrite instead of blocking;
        // the request that offers the work is never made to wait for capacity.
        _channel = Channel.CreateBounded<VerificationDeliveryRequest>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public bool TryEnqueue(VerificationDeliveryRequest request)
    {
        if (_channel.Writer.TryWrite(request))
        {
            _counters.MarkAccepted();
            return true;
        }

        // The queue is full, or it is shutting down. The request is dropped: the anonymous
        // caller is not told anything, because the answer is the same in every case.
        _counters.MarkRejected();

        if (_counters.Rejected % RefusalLogInterval == 1)
        {
            _logger.LogWarning(
                "The verification delivery queue refused a request. Accepted: {Accepted}, refused: {Rejected}.",
                _counters.Accepted,
                _counters.Rejected);
        }

        return false;
    }

    public void Complete() => _channel.Writer.TryComplete();

    public IAsyncEnumerable<VerificationDeliveryRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public int DrainRemaining()
    {
        var drained = 0;
        while (_channel.Reader.TryRead(out _))
        {
            drained++;
        }

        return drained;
    }

    public int Pending => _channel.Reader.Count;
}
