using Microsoft.Data.SqlClient;

namespace Nibras.CleanDbMigrator;

/// <summary>
/// Reads the actual server identity and target state before any DDL. It connects to
/// <c>master</c> to confirm the instance and whether the database exists, then (only when it
/// exists) to the target itself to confirm <c>DB_NAME()</c>/<c>DB_ID()</c> and the migration
/// state. Every command is read-only.
/// </summary>
internal static class CleanDatabaseInspector
{
    public static async Task<CleanDatabaseState> InspectAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var requestedDatabase = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (string.IsNullOrWhiteSpace(requestedDatabase))
        {
            throw new InvalidOperationException("The connection string does not name a database.");
        }

        var masterConnectionString = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString;

        string serverName;
        int? databaseId;
        await using (var master = new SqlConnection(masterConnectionString))
        {
            await master.OpenAsync(cancellationToken);
            serverName = Convert.ToString(await ScalarAsync(master, "SELECT @@SERVERNAME;", cancellationToken))
                         ?? string.Empty;
            databaseId = await ScalarIntAsync(master, "SELECT DB_ID(@name);", requestedDatabase, cancellationToken);
        }

        if (databaseId is null)
        {
            return new CleanDatabaseState(
                serverName,
                requestedDatabase,
                DatabaseExists: false,
                DatabaseId: null,
                HistoryTableExists: false,
                AppliedMigrationCount: 0,
                UserTableCount: 0,
                Collation: null,
                CompatibilityLevel: null);
        }

        await using var target = new SqlConnection(connectionString);
        await target.OpenAsync(cancellationToken);

        var actualDatabaseName = Convert.ToString(await ScalarAsync(target, "SELECT DB_NAME();", cancellationToken))
                                 ?? string.Empty;
        var actualDatabaseId = await ScalarIntAsync(target, "SELECT DB_ID();", null, cancellationToken);

        var historyExists = Convert.ToInt32(await ScalarAsync(
            target,
            "SELECT CASE WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THEN 0 ELSE 1 END;",
            cancellationToken)) == 1;

        var appliedCount = historyExists
            ? Convert.ToInt32(await ScalarAsync(
                target,
                "SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory];",
                cancellationToken))
            : 0;

        var userTableCount = Convert.ToInt32(await ScalarAsync(
            target,
            "SELECT COUNT(*) FROM sys.tables WHERE name <> N'__EFMigrationsHistory';",
            cancellationToken));

        var collation = Convert.ToString(await ScalarAsync(
            target,
            "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(256));",
            cancellationToken));

        var compatibility = await ScalarIntAsync(
            target,
            "SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME();",
            null,
            cancellationToken);

        return new CleanDatabaseState(
            serverName,
            actualDatabaseName,
            DatabaseExists: true,
            actualDatabaseId,
            historyExists,
            appliedCount,
            userTableCount,
            collation,
            compatibility);
    }

    private static async Task<object?> ScalarAsync(
        SqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(commandText, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    private static async Task<int?> ScalarIntAsync(
        SqlConnection connection,
        string commandText,
        string? databaseNameParameter,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(commandText, connection);
        if (databaseNameParameter is not null)
        {
            command.Parameters.AddWithValue("@name", databaseNameParameter);
        }

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }
}
