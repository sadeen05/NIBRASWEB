namespace Nibras.CleanDbMigrator;

/// <summary>
/// Read-only snapshot of the actual target identity and migration state, captured before
/// any DDL. The server name and database name are the values the database itself reports,
/// not the requested connection string.
/// </summary>
internal sealed record CleanDatabaseState(
    string ServerName,
    string DatabaseName,
    bool DatabaseExists,
    int? DatabaseId,
    bool HistoryTableExists,
    int AppliedMigrationCount,
    int UserTableCount,
    string? Collation,
    int? CompatibilityLevel)
{
    /// <summary>Whether the target already holds schema/history that must not be overwritten silently.</summary>
    public bool IsNonEmptyOrPartiallyMigrated =>
        HistoryTableExists || UserTableCount > 0 || AppliedMigrationCount > 0;
}
