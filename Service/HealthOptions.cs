namespace NIBRAS.API.Services;

/// <summary>
/// Configuration of the operational health endpoints. The readiness window is the only
/// value: every probe must answer inside it, so a stalled database can never hold the
/// monitoring request itself open.
/// </summary>
public sealed class HealthOptions
{
    public const string SectionName = "Health";

    /// <summary>The tag that marks a registration as part of the readiness probe.</summary>
    public const string ReadinessTag = "ready";

    /// <summary>
    /// The bounded window of the database readiness check, in seconds. It must be far
    /// below the monitoring platform's own timeout so the probe is never the slowest
    /// component. Defaults to 10 seconds: the first probe of a cold process also pays
    /// the connection-pool initialization cost, which measured around three seconds
    /// locally, and a flapping first answer would pull the instance before it ever
    /// served. Operators may raise or lower it; the check clamps it to at least one
    /// second and never removes the bound.
    /// </summary>
    public int DatabaseTimeoutSeconds { get; set; } = 10;
}
