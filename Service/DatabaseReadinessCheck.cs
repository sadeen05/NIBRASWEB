using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

/// <summary>
/// The readiness dependency check: it proves the essential runtime dependencies of the API
/// are answerable, using only a cheap, bounded database round trip.
/// The check never sends mail (SMTP availability is a delivery concern, not a readiness
/// concern, and no external SMTP traffic may be produced by a probe) and never touches the
/// document storage. The outcome is deliberately coarse: it never contains a connection
/// string, a server name, or an exception trace, so the answer can be exposed to a reverse
/// proxy or monitoring platform without leaking infrastructure details. The failure
/// category is logged without any sensitive material.
/// </summary>
public sealed class DatabaseReadinessCheck : IHealthCheck
{
    private readonly NebrasdbContext _context;
    private readonly HealthOptions _options;
    private readonly ILogger<DatabaseReadinessCheck> _logger;

    public DatabaseReadinessCheck(
        NebrasdbContext context,
        IOptions<HealthOptions> options,
        ILogger<DatabaseReadinessCheck> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // The window is bounded on both ends: a misconfigured or extreme value can only
        // make the probe stricter (at least one second), never unbounded.
        var windowSeconds = Math.Max(1, _options.DatabaseTimeoutSeconds);

        try
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(TimeSpan.FromSeconds(windowSeconds));
            await _context.Database.ExecuteSqlRawAsync("SELECT 1;", bounded.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The process itself is shutting down: the answer is the same closed verdict,
            // without any detail.
            _logger.LogWarning("The database readiness check was interrupted by process shutdown.");
            return HealthCheckResult.Unhealthy("The readiness check did not complete.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "The database readiness check exceeded its {WindowSeconds}s window. " +
                "No server, database or error detail is included in the response.",
                windowSeconds);
            return HealthCheckResult.Unhealthy("The database did not answer within the readiness window.");
        }
        catch (Exception exception)
        {
            // The log carries the failure category, not the exception detail: database
            // errors can embed the server or network naming of the infrastructure.
            _logger.LogWarning(
                exception,
                "The database readiness check failed. No infrastructure detail is included in the response.");
            return HealthCheckResult.Unhealthy("The database is not reachable.");
        }
    }
}
