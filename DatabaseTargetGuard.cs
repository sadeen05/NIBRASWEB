using Microsoft.Data.SqlClient;
using System.Text;

namespace NIBRAS;

/// <summary>
/// Startup safety guard for the resolved database target.
/// The production-like database is denied in every environment by default and is
/// permitted only when its exact name is explicitly listed as approved through
/// deployment configuration. The environment name is never consulted, so a missing
/// or Production environment is never treated as authorization.
/// The effective target is read with SQL Server's own connection-string parser
/// (<see cref="Microsoft.Data.SqlClient.SqlConnectionStringBuilder"/>).
/// This type never opens a database connection.
/// </summary>
public static class DatabaseTargetGuard
{
    /// <summary>
    /// Legacy production-like database name that is denied unless its exact name is
    /// explicitly approved. Kept for compatibility; see
    /// <see cref="ProductionLikeDatabaseNames"/> for the complete protected set.
    /// </summary>
    public const string ProductionLikeDatabaseName = "NEBRASDB";

    /// <summary>
    /// Every protected official / production-like database name. Each name is denied by
    /// default in every environment and requires its exact name in
    /// <see cref="ApprovedProductionLikeTargetsKey"/> before the application may use it.
    /// This set includes both the legacy production-like name and the clean official
    /// database <see cref="CleanDatabase.DatabaseName"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> ProductionLikeDatabaseNames =
        new[] { ProductionLikeDatabaseName, CleanDatabase.DatabaseName };

    /// <summary>
    /// Historical/shared databases that are refused in every environment and cannot be
    /// approved through configuration. They are not valid deployment or test targets.
    /// </summary>
    private static readonly string[] AlwaysForbiddenDatabaseNames = { "NEBRAS", "NEBRASDB_Step1" };

    /// <summary>
    /// Configuration key holding the deployment-approved list of production-like
    /// database names (comma- or semicolon-separated). Empty by default.
    /// Deployment authorization is granted by listing the exact database name, e.g.
    /// <c>Database:ApprovedProductionLikeTargets=NEBRASDB</c> (environment variable
    /// <c>Database__ApprovedProductionLikeTargets</c>).
    /// </summary>
    public const string ApprovedProductionLikeTargetsKey = "Database:ApprovedProductionLikeTargets";

    private static readonly string[] DatabaseKeys =
        { "database", "initial catalog" };

    /// <summary>
    /// Validates the resolved connection string and returns a sanitized target
    /// (server and database name only). Throws <see cref="InvalidOperationException"/>
    /// when the connection string is missing or invalid, or when the target is the
    /// production-like database and its exact name is not explicitly approved.
    /// Does not open a database connection.
    /// </summary>
    /// <param name="connectionString">The resolved ConnectionStrings:DefaultConnection value.</param>
    /// <param name="approvedProductionLikeTargets">
    /// Explicit, deployment-approved list of production-like database names.
    /// A missing, blank or malformed value approves nothing.
    /// </param>
    public static DatabaseTarget Validate(
        string? connectionString,
        string? approvedProductionLikeTargets)
    {
        var target = Parse(connectionString);

        if (IsAlwaysForbidden(target.Database))
        {
            throw new InvalidOperationException(
                $"Refusing to use the forbidden historical/shared database '{target.Database}'. " +
                "This name is denied in every environment and cannot be approved through configuration. " +
                "Point ConnectionStrings:DefaultConnection at the explicitly approved deployment target.");
        }

        if (target.IsProductionLike && !IsApproved(target.Database, approvedProductionLikeTargets))
        {
            throw new InvalidOperationException(
                $"Refusing to use the production-like database '{target.Database}'. It is denied by default in " +
                "all environments. Point ConnectionStrings:DefaultConnection (or the " +
                "ConnectionStrings__DefaultConnection environment variable) at an approved test database, or, for " +
                "an intentional deployment, list the exact database name in " +
                $"{ApprovedProductionLikeTargetsKey}.");
        }

        return target;
    }

    /// <summary>
    /// Whether the name is one of the historical/shared databases that no configuration can
    /// approve. The comparison is exact and case-insensitive.
    /// </summary>
    public static bool IsAlwaysForbidden(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return false;
        }

        foreach (var forbidden in AlwaysForbiddenDatabaseNames)
        {
            if (string.Equals(databaseName.Trim(), forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="databaseName"/> is explicitly approved. Only an exact,
    /// case-insensitive match in the configured list counts; a missing, blank or
    /// otherwise malformed value approves nothing.
    /// </summary>
    public static bool IsApproved(string? databaseName, string? approvedProductionLikeTargets)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(approvedProductionLikeTargets))
        {
            return false;
        }

        foreach (var entry in approvedProductionLikeTargets.Split(
                     new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(entry.Trim(), databaseName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the name is a protected official / production-like database
    /// (see <see cref="ProductionLikeDatabaseNames"/>). The comparison is exact and
    /// case-insensitive; a missing or blank name is not production-like.
    /// </summary>
    public static bool IsProductionLike(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return false;
        }

        foreach (var name in ProductionLikeDatabaseNames)
        {
            if (string.Equals(databaseName.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Refuses a Development seeding path against a protected official / production-like
    /// database. The clean official database must never receive Development seed or demo
    /// data, and the same rule protects the legacy production-like database. The connection
    /// string is parsed but no connection is opened.
    /// </summary>
    public static void RefuseSeedingOnProtectedDatabase(string? connectionString, string seederName)
    {
        var target = Parse(connectionString);
        if (IsProductionLike(target.Database))
        {
            throw new InvalidOperationException(
                $"{seederName} refused: '{target.Database}' is a protected official database. " +
                "Development seeding and demo-data creation must never run against it.");
        }
    }

    /// <summary>
    /// Extracts the sanitized <see cref="DatabaseTarget"/> (server and database name
    /// only) from a connection string. Ambiguous input (repeated or mixed
    /// 'Database'/'Initial Catalog' keys) is rejected before SQL Server's own parser can
    /// silently resolve it, and the effective target is then read from that parser
    /// (<see cref="SqlConnectionStringBuilder"/>). Malformed, empty or unsupported input
    /// fails safe by throwing. No raw parser detail is surfaced. Does not open a
    /// database connection.
    /// </summary>
    public static DatabaseTarget Parse(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. Set it in configuration or via the " +
                "ConnectionStrings__DefaultConnection environment variable.");
        }

        // Reject ambiguity in the raw string first: SQL Server's parser resolves repeated
        // or mixed database keys by last-wins, so it must not be consulted before this check.
        var databaseKeys = 0;
        foreach (var (key, _) in Tokenize(connectionString))
        {
            if (Matches(key, DatabaseKeys))
            {
                databaseKeys++;
            }
        }

        if (databaseKeys == 0)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection does not specify a database name.");
        }

        if (databaseKeys > 1)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection specifies more than one database name " +
                "(a repeated key or a mix of 'Database' and 'Initial Catalog'). The database " +
                "target is ambiguous and is refused.");
        }

        // Effective target comes from SQL Server's own parser; raw parser errors are not surfaced.
        string server;
        string database;
        try
        {
            var parsed = new SqlConnectionStringBuilder(connectionString);
            server = parsed.DataSource;
            database = parsed.InitialCatalog;
        }
        catch (Exception)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not a valid SQL Server connection string.");
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection does not specify a database name.");
        }

        return new DatabaseTarget(server, database);
    }

    private static bool Matches(string key, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<KeyValuePair<string, string>> Tokenize(string connectionString)
    {
        var index = 0;
        while (index < connectionString.Length)
        {
            while (index < connectionString.Length &&
                   (connectionString[index] == ';' || char.IsWhiteSpace(connectionString[index])))
            {
                index++;
            }

            if (index >= connectionString.Length)
            {
                yield break;
            }

            var keyStart = index;
            while (index < connectionString.Length &&
                   connectionString[index] != '=' && connectionString[index] != ';')
            {
                index++;
            }

            var key = connectionString.Substring(keyStart, index - keyStart).Trim();

            if (index >= connectionString.Length || connectionString[index] != '=')
            {
                continue;
            }

            index++;

            string value;
            if (index < connectionString.Length && (connectionString[index] == '\'' || connectionString[index] == '"'))
            {
                var quote = connectionString[index];
                index++;
                var builder = new StringBuilder();
                while (index < connectionString.Length && connectionString[index] != quote)
                {
                    builder.Append(connectionString[index]);
                    index++;
                }
                if (index < connectionString.Length)
                {
                    index++;
                }
                value = builder.ToString().Trim();
            }
            else
            {
                var valueStart = index;
                while (index < connectionString.Length && connectionString[index] != ';')
                {
                    index++;
                }
                value = connectionString.Substring(valueStart, index - valueStart).Trim();
            }

            if (key.Length > 0)
            {
                yield return new KeyValuePair<string, string>(key, value);
            }
        }
    }
}

/// <summary>
/// Sanitized database target: server and database name only, never credentials.
/// </summary>
public readonly record struct DatabaseTarget(string Server, string Database)
{
    /// <summary>Whether the target is a protected official / production-like database.</summary>
    public bool IsProductionLike =>
        DatabaseTargetGuard.IsProductionLike(Database);
}
