using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NIBRAS.API.Services;
using NIBRAS.Models;

namespace NibrasWeb.F1.Preparation;

// Deterministic checks of the operational health endpoints' readiness logic. The database
// dependency is a tiny fake whose command behavior is chosen per test; no database is
// contacted and no mail is sent. The coarse, detail-free answer shape is asserted directly.
public static class HealthCheckMocks
{
    public static async Task RunAsync()
    {
        Program.Check(new HealthOptions().DatabaseTimeoutSeconds == 10,
            "readiness window defaults to a bounded ten seconds");
        await HealthyProbeAsync();
        await UnreachabilityProbeAsync();
        await TimedOutProbeAsync();
        await ClampedWindowProbeAsync();
        await ShutdownProbeAsync();
    }

    private static DatabaseReadinessCheck Check(IOptions<HealthOptions> options, DbConnection connection) =>
        new(
            new NebrasdbContext(new DbContextOptionsBuilder<NebrasdbContext>()
                .UseSqlServer(connection)
                .Options),
            options,
            NullLogger<DatabaseReadinessCheck>.Instance);

    private static async Task HealthyProbeAsync()
    {
        using var connection = new Connection(new ThreadingControl { Mode = ModeSignal.Immediate });
        var result = await Check(Options.Create(new HealthOptions()), connection)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
        Program.Check(result.Status == HealthStatus.Healthy && string.IsNullOrEmpty(result.Description),
            "an answering database is healthy and carries no detail");
    }

    private static async Task UnreachabilityProbeAsync()
    {
        using var connection = new Connection(new ThreadingControl
        {
            Mode = ModeSignal.Throw,
            Failure = new UnreachableHostException(),
        });
        var result = await Check(Options.Create(new HealthOptions()), connection)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
        Program.Check(result.Status == HealthStatus.Unhealthy,
            "an unreachable database is unhealthy");
        Program.Check(result.Description == "The database is not reachable.",
            "unreachable database exposes no exception text or infrastructure naming");
    }

    private static async Task TimedOutProbeAsync()
    {
        using var connection = new Connection(new ThreadingControl { Mode = ModeSignal.Hang });
        var clock = Stopwatch.StartNew();
        var result = await Check(Options.Create(new HealthOptions { DatabaseTimeoutSeconds = 1 }), connection)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
        clock.Stop();
        Program.Check(result.Status == HealthStatus.Unhealthy,
            "a database that never answers is unhealthy");
        Program.Check(result.Description == "The database did not answer within the readiness window.",
            "a timed-out database exposes no infrastructure detail");
        Program.Check(clock.Elapsed.TotalSeconds < 10,
            "the readiness probe itself is bounded");
    }

    private static async Task ClampedWindowProbeAsync()
    {
        // A misconfigured window can only become stricter (at least one second), never
        // unbounded.
        using var connection = new Connection(new ThreadingControl { Mode = ModeSignal.Hang });
        var clock = Stopwatch.StartNew();
        var result = await Check(Options.Create(new HealthOptions { DatabaseTimeoutSeconds = 0 }), connection)
            .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
        clock.Stop();
        Program.Check(result.Status == HealthStatus.Unhealthy && clock.Elapsed.TotalSeconds < 10,
            "a zero readiness window is clamped to a bounded one-second probe");
    }

    private static async Task ShutdownProbeAsync()
    {
        using var connection = new Connection(new ThreadingControl { Mode = ModeSignal.Hang });
        using var shuttingDown = new CancellationTokenSource();
        shuttingDown.Cancel();
        var result = await Check(Options.Create(new HealthOptions()), connection)
            .CheckHealthAsync(new HealthCheckContext(), shuttingDown.Token);
        Program.Check(result.Status == HealthStatus.Unhealthy &&
            result.Description == "The readiness check did not complete.",
            "a process shutdown interrupts the probe with the same closed verdict");
    }

    private enum ModeSignal { Immediate, Throw, Hang }

    /// <summary>Why the probe answered the way it did.</summary>
    private sealed class UnreachableHostException : Exception
    {
        public UnreachableHostException() : base("simulated network failure with private details 10.0.0.1:1433") { }
    }

    private sealed class ThreadingControl
    {
        public ModeSignal Mode { get; init; }
        public Exception? Failure { get; init; }
    }

    private sealed class Connection(ThreadingControl control) : DbConnection
    {
        private ConnectionState state;
        [AllowNull] public override string ConnectionString { get; set; } = "Server=offline;Database=health";
        public override string Database => "health";
        public override string DataSource => "no-network";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => state;
        public override void Open() => state = ConnectionState.Open;
        public override void Close() => state = ConnectionState.Closed;
        public override void ChangeDatabase(string name) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new Command(control);
    }

    private sealed class Command(ThreadingControl control) : DbCommand
    {
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new MarkerParameterCollection();
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        public override void Cancel() { }
        public override void Prepare() { }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => control.Mode switch
        {
            ModeSignal.Immediate => Task.FromResult(1),
            ModeSignal.Throw => throw control.Failure!,
            _ => HangAsync(cancellationToken),
        };

        private static async Task<int> HangAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 1;
        }

        public override int ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();
        public override object ExecuteScalar() => ExecuteNonQuery();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw control.Mode switch
            {
                ModeSignal.Immediate => new InvalidOperationException("The driver cannot read."),
                _ => control.Failure ?? new UnreachableHostException(),
            };
        protected override void Dispose(bool disposing) => base.Dispose(disposing);
    }

    /// <summary>Minimal parameter collection for the fake command.</summary>
    private sealed class MarkerParameterCollection : DbParameterCollection
    {
        public override int Count => 0;
        public override object SyncRoot => this;
        public override int Add(object value) => throw new NotSupportedException();
        public override void AddRange(Array values) => throw new NotSupportedException();
        public override void Clear() { }
        public override bool Contains(object value) => false;
        public override bool Contains(string parameterName) => false;
        public override void CopyTo(Array array, int index) { }
        public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
        public override int IndexOf(object value) => -1;
        public override int IndexOf(string parameterName) => -1;
        public override void Insert(int index, object value) => throw new NotSupportedException();
        public override void Remove(object value) { }
        public override void RemoveAt(int index) { }
        public override void RemoveAt(string parameterName) { }
        protected override DbParameter GetParameter(int index) => throw new NotSupportedException();
        protected override void SetParameter(int index, DbParameter value) => throw new NotSupportedException();
        protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
        protected override void SetParameter(string parameterName, DbParameter value) => throw new NotSupportedException();
    }
}
