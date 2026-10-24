namespace NIBRAS;

/// <summary>
/// Fail-closed targeting for the clean database migration tooling. Unlike the application's
/// <see cref="DatabaseTargetGuard"/> (which admits every target that is not production-like),
/// this guard admits exactly one target: <see cref="CleanDatabase.ServerName"/> /
/// <see cref="CleanDatabase.DatabaseName"/>. Every other server or database name is refused.
/// It parses the connection string but never opens a database connection, so it can run
/// before the target is touched at all.
/// </summary>
public static class CleanDatabaseTargetGuard
{
    /// <summary>
    /// Validates a clean-database connection string and returns its sanitized target.
    /// Throws <see cref="InvalidOperationException"/> for a missing, ambiguous or
    /// unparsable connection string, and for any server or database that is not the exact
    /// clean target.
    /// </summary>
    public static DatabaseTarget Validate(string? connectionString)
    {
        var target = DatabaseTargetGuard.Parse(connectionString);
        ValidateTarget(target);
        return target;
    }

    /// <summary>
    /// Validates an already-parsed target. Throws for a wrong server, a wrong database, or
    /// one of the always-forbidden historical/shared names.
    /// </summary>
    public static void ValidateTarget(DatabaseTarget target)
    {
        if (DatabaseTargetGuard.IsAlwaysForbidden(target.Database))
        {
            throw new InvalidOperationException(
                $"Refusing the forbidden historical/shared database '{target.Database}'. " +
                "It is denied in every environment and can never be approved.");
        }

        if (!IsAcceptedServer(target.Server))
        {
            throw new InvalidOperationException(
                $"Refusing target server '{target.Server}'. The clean database runner may only " +
                $"target the exact instance '{CleanDatabase.ServerName}'.");
        }

        if (!IsAcceptedDatabase(target.Database))
        {
            throw new InvalidOperationException(
                $"Refusing target database '{target.Database}'. The clean database runner may only " +
                $"target the exact database '{CleanDatabase.DatabaseName}'.");
        }
    }

    /// <summary>Whether the server is one of the accepted clean-database instance names.</summary>
    public static bool IsAcceptedServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return false;
        }

        foreach (var name in CleanDatabase.AcceptedServerNames)
        {
            if (string.Equals(server.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the database name is the exact clean database name.</summary>
    public static bool IsAcceptedDatabase(string? database)
    {
        return !string.IsNullOrWhiteSpace(database)
               && string.Equals(database.Trim(), CleanDatabase.DatabaseName, StringComparison.OrdinalIgnoreCase);
    }
}
