namespace Nibras.CleanDbMigrator;

/// <summary>
/// Entry point of the clean database migration runner. It is tooling only: it is never
/// started by the web host, it never seeds, and it refuses every target except
/// <see cref="NIBRAS.CleanDatabase.ServerName"/> / <see cref="NIBRAS.CleanDatabase.DatabaseName"/>.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        MigratorArguments options;
        try
        {
            options = MigratorArguments.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            MigratorArguments.PrintHelp(Console.Error);
            return CleanDatabaseMigrator.ExitConfigurationInvalid;
        }

        if (options.ShowHelp)
        {
            MigratorArguments.PrintHelp(Console.Out);
            return CleanDatabaseMigrator.ExitSuccess;
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Console.Error.WriteLine(
                $"Missing connection string. Pass --connection or set {MigratorArguments.ConnectionEnvironmentVariable}.");
            return CleanDatabaseMigrator.ExitConfigurationInvalid;
        }

        try
        {
            var migrator = new CleanDatabaseMigrator(Console.Out);
            return await migrator.RunAsync(options.ConnectionString, options, CancellationToken.None);
        }
        catch (CleanDatabaseRefusalException refusal)
        {
            Console.Error.WriteLine(refusal.Message);
            return refusal.ExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Clean database migration failed: {exception.GetType().Name}: {exception.Message}");
            return CleanDatabaseMigrator.ExitFailure;
        }
    }
}
