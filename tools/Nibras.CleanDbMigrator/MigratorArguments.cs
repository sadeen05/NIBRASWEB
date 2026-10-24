namespace Nibras.CleanDbMigrator;

/// <summary>
/// Parsed command-line arguments for the clean database runner. The connection string is
/// never read from <c>appsettings.json</c>: it comes from <c>--connection</c> or the
/// dedicated environment variable, so the tool can never be pointed at the application's
/// configured target by accident.
/// </summary>
internal sealed class MigratorArguments
{
    public const string ConnectionEnvironmentVariable = "NIBRAS_CLEAN_DB_CONNECTION";

    public string? ConnectionString { get; private init; }
    public bool Apply { get; private init; }
    public bool AllowExisting { get; private init; }
    public bool ShowHelp { get; private init; }

    public static MigratorArguments Parse(string[] args)
    {
        string? connection = null;
        var apply = false;
        var allowExisting = false;
        var help = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--connection":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--connection requires a value.");
                    }

                    connection = args[++index];
                    break;

                case "--apply":
                    apply = true;
                    break;

                case "--allow-existing":
                    allowExisting = true;
                    break;

                case "--help":
                case "-h":
                    help = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }

        connection ??= Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);

        return new MigratorArguments
        {
            ConnectionString = connection,
            Apply = apply,
            AllowExisting = allowExisting,
            ShowHelp = help,
        };
    }

    public static void PrintHelp(TextWriter writer)
    {
        writer.WriteLine("Nibras clean database migration runner");
        writer.WriteLine();
        writer.WriteLine($"Target: {NIBRAS.CleanDatabase.ServerName} / {NIBRAS.CleanDatabase.DatabaseName}");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  Nibras.CleanDbMigrator [--connection <string>] [--apply] [--allow-existing]");
        writer.WriteLine();
        writer.WriteLine("  --connection <string>  SQL Server connection string. Defaults to the");
        writer.WriteLine($"                         {ConnectionEnvironmentVariable} environment variable.");
        writer.WriteLine("  --apply                Create/migrate the target. Without it the runner only");
        writer.WriteLine("                         validates the target and inspects the current state (dry run).");
        writer.WriteLine("  --allow-existing       Permit an existing non-empty or partially migrated target.");
        writer.WriteLine("                         Requires separate explicit approval.");
        writer.WriteLine("  --help                 Show this help.");
        writer.WriteLine();
        writer.WriteLine("Exit codes: 0 success, 1 failure, 2 invalid configuration/target, " +
                       "3 refused target state, 4 migration failed.");
    }
}
