namespace NIBRAS;

/// <summary>
/// The single authoritative clean NIBRAS database. The name is exact and deliberately
/// spelled this way; every migration runner, configuration value and clean-database test
/// must use these constants so the target can never drift.
/// <para>
/// The clean database is built from the reviewed 21-migration model and starts with only the
/// migration reference data (roles, governorates, document types) plus the unreachable
/// <c>system@nibras.internal</c> placeholder. It must never receive Development seed or demo
/// data. The illustrative <c>LAND_CRITERIA</c> seed values are <b>not</b> approved production
/// policy: the required engineering-policy decision must be recorded before any real land
/// evaluation (see docs/CLEAN_DATABASE_PLAN.md).
/// </para>
/// </summary>
public static class CleanDatabase
{
    /// <summary>The exact SQL Server instance the clean database lives on.</summary>
    public const string ServerName = @"SADEEN\SQLEXPRESS";

    /// <summary>The exact clean database name (intentionally spelled "cleanvirsion").</summary>
    public const string DatabaseName = "NIBRAS_cleanvirsion";

    /// <summary>
    /// Server names accepted as the clean database instance. The machine-name form of the
    /// same instance is accepted because it resolves to the identical server.
    /// </summary>
    public static readonly IReadOnlyList<string> AcceptedServerNames = new[]
    {
        ServerName,
        $@"{Environment.MachineName}\SQLEXPRESS",
    };
}
