namespace Nibras.CleanDbMigrator;

/// <summary>
/// A controlled refusal or migration failure carrying the documented process exit code.
/// </summary>
internal sealed class CleanDatabaseRefusalException : Exception
{
    public CleanDatabaseRefusalException(int exitCode, string message)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
