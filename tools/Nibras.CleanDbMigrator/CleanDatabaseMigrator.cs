using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.Models;

namespace Nibras.CleanDbMigrator;

/// <summary>
/// Target-pinned, fail-closed runner for the clean official database. It:
/// <list type="number">
/// <item>validates the connection string against <see cref="CleanDatabaseTargetGuard"/> before any connection;</item>
/// <item>confirms the actual server/database identity and inspects the existing state read-only;</item>
/// <item>refuses an existing non-empty or partially migrated target unless explicitly allowed;</item>
/// <item>applies the model migrations with EF Core command-by-command execution
/// (<see cref="RelationalDatabaseFacadeExtensions.MigrateAsync"/>), which preserves migration
/// ordering and applies each migration in its own transaction;</item>
/// <item>verifies that the applied history equals the model and that no migration is pending.</item>
/// </list>
/// It never calls <c>EnsureCreated</c>, never rewrites history, and never seeds.
/// </summary>
internal sealed class CleanDatabaseMigrator
{
    public const int ExitSuccess = 0;
    public const int ExitFailure = 1;
    public const int ExitConfigurationInvalid = 2;
    public const int ExitRefused = 3;
    public const int ExitMigrationFailed = 4;

    private readonly TextWriter _output;

    public CleanDatabaseMigrator(TextWriter output)
    {
        _output = output;
    }

    public async Task<int> RunAsync(
        string connectionString,
        MigratorArguments options,
        CancellationToken cancellationToken)
    {
        // 1. Fail-closed targeting from the connection string, before any connection is opened.
        DatabaseTarget target;
        try
        {
            target = CleanDatabaseTargetGuard.Validate(connectionString);
        }
        catch (InvalidOperationException exception)
        {
            throw new CleanDatabaseRefusalException(ExitConfigurationInvalid, exception.Message);
        }

        _output.WriteLine($"Clean database target: {target.Server}/{target.Database}");

        // 2. Confirm the real identity and inspect state read-only before any DDL.
        CleanDatabaseState state;
        try
        {
            state = await CleanDatabaseInspector.InspectAsync(connectionString, cancellationToken);
        }
        catch (Exception exception)
        {
            throw new CleanDatabaseRefusalException(
                ExitConfigurationInvalid,
                $"Unable to inspect the target before DDL: {exception.GetType().Name}: {exception.Message}");
        }

        if (!CleanDatabaseTargetGuard.IsAcceptedServer(state.ServerName))
        {
            throw new CleanDatabaseRefusalException(
                ExitConfigurationInvalid,
                $"Refusing actual server '{state.ServerName}'. Expected '{CleanDatabase.ServerName}'.");
        }

        if (state.DatabaseExists)
        {
            if (!CleanDatabaseTargetGuard.IsAcceptedDatabase(state.DatabaseName))
            {
                throw new CleanDatabaseRefusalException(
                    ExitConfigurationInvalid,
                    $"Refusing actual database '{state.DatabaseName}'. Expected '{CleanDatabase.DatabaseName}'.");
            }

            _output.WriteLine(
                $"Existing target: DB_ID={state.DatabaseId}, applied={state.AppliedMigrationCount}, " +
                $"userTables={state.UserTableCount}, history={(state.HistoryTableExists ? "yes" : "no")}, " +
                $"collation={state.Collation}, compatibility={state.CompatibilityLevel}.");

            if (state.IsNonEmptyOrPartiallyMigrated && !options.AllowExisting)
            {
                throw new CleanDatabaseRefusalException(
                    ExitRefused,
                    $"Refusing the existing non-empty or partially migrated database '{state.DatabaseName}'. " +
                    "Re-run with --allow-existing only under separate explicit approval.");
            }
        }
        else
        {
            _output.WriteLine("Target does not exist; EF Core will create it and apply the ordered migrations.");
        }

        if (!options.Apply)
        {
            _output.WriteLine("Dry run complete: no DDL executed. Re-run with --apply to create/migrate.");
            return ExitSuccess;
        }

        // 3. EF Core command-by-command migration: ordered, each migration in its own transaction.
        var dbOptions = new DbContextOptionsBuilder<NebrasdbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var context = new NebrasdbContext(dbOptions);

        var modelMigrations = context.Database.GetMigrations().ToList();
        if (modelMigrations.Count == 0)
        {
            throw new CleanDatabaseRefusalException(ExitConfigurationInvalid, "The model defines no migrations.");
        }

        _output.WriteLine($"Model migrations: {modelMigrations.Count}; final {modelMigrations[^1]}.");

        try
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new CleanDatabaseRefusalException(
                ExitMigrationFailed,
                $"Migration failed: {exception.GetType().Name}: {exception.Message}");
        }

        // 4. Verify ordering, completeness and the final applied state.
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count != 0)
        {
            throw new CleanDatabaseRefusalException(
                ExitMigrationFailed,
                $"Migration verification failed: {pending.Count} migration(s) remain pending.");
        }

        if (!applied.SequenceEqual(modelMigrations, StringComparer.Ordinal))
        {
            throw new CleanDatabaseRefusalException(
                ExitMigrationFailed,
                "Migration verification failed: the applied history does not match the model.");
        }

        var after = await CleanDatabaseInspector.InspectAsync(connectionString, cancellationToken);
        _output.WriteLine($"Applied migrations: {applied.Count}; final {applied[^1]}.");
        _output.WriteLine(
            $"Post-migration: DB_ID={after.DatabaseId}, userTables={after.UserTableCount}, " +
            $"collation={after.Collation}, compatibility={after.CompatibilityLevel}.");
        _output.WriteLine("Clean database migration completed successfully.");
        return ExitSuccess;
    }
}
