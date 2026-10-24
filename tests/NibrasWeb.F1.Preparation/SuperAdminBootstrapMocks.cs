using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NIBRAS.API.Services;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NibrasWeb.F1.Preparation;

// Deterministic, offline checks of the one-time SuperAdmin bootstrap. They share one tiny
// in-memory database fake that understands exactly the SQL shapes the bootstrap issues:
// the locking ROLE pre-check, the users predicate, the EXISTS existence probes and the two
// inserts. Everything else fails the test instead of being guessed. No database, role
// account or audit row is created outside this fake.
public static class SuperAdminBootstrapMocks
{
    private const string OperatorEmail = "operator@example.test";
    private const string OperatorPassword = "Bootstrap-P@ssw0rd-2026";

    public static async Task RunAsync()
    {
        await ConfigurationRefusalsAsync();
        await SucceedsOnceAsync();
        await RefusesExistingSuperAdminAsync();
        await RefusesTakenIdentityAsync();
        await ConcurrentAttemptsAsync();
        GuardChecks.Run();
        SuperAdminInvocationChecks.Run();
    }

    // ---------------------------------------------------------------- fixtures

    private static Role SuperRole() => new() { Id = 4, Name = RoleNames.SuperAdmin };

    private static Role AdminRole() => new() { Id = 3, Name = RoleNames.Admin };

    /// <summary>The model-seeded system account: it can never authenticate anybody.</summary>
    private static User Placeholder() => new()
    {
        Id = -1,
        RoleId = 4,
        FullName = "System",
        Email = "system@nibras.internal",
        Phone = "0000000000",
        PasswordHash = "N/A",
        IsDeleted = false,
        IsApproved = false,
        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static User Landlord() => new()
    {
        Id = 7,
        RoleId = 1,
        FullName = "Existing Landlord",
        Email = "landlord@example.test",
        Phone = "5555555555",
        PasswordHash = Password("fixture-password"),
        IsDeleted = false,
        IsApproved = true,
    };

    private static string Password(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4);

    // ---------------------------------------------------------------- plumbing

    private static IServiceProvider Providers(
        BootstrapStore store,
        CapturedLogs logs)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new CapturedLoggerFactory(logs));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddDbContext<NebrasdbContext>(options =>
            options.UseSqlServer(new Connection(store)).AddInterceptors(new Save(store)));
        services.AddScoped<IUserSecurityEventService, UserSecurityEventService>();
        return services.BuildServiceProvider();
    }

    /// <summary>Runs the bootstrap with the credentials taken from the fake's own configuration.</summary>
    private static Task<int> InvokeConfiguredAsync(BootstrapStore store, CapturedLogs logs) =>
        InvokeRawAsync(store, logs, store.Email, store.Password, confirmation: store.Email);

    /// <summary>Runs the bootstrap with explicit credential values.</summary>
    private static Task<int> InvokeAsync(
        BootstrapStore store,
        CapturedLogs logs,
        string? email = OperatorEmail,
        string? password = OperatorPassword,
        string? confirmation = OperatorEmail) =>
        InvokeRawAsync(store, logs, email, password, confirmation);

    private static Task<int> InvokeRawAsync(
        BootstrapStore store,
        CapturedLogs logs,
        string? email,
        string? password,
        string? confirmation)
    {
        var values = new Dictionary<string, string?>();
        if (email is not null) values[NIBRAS.API.Services.SuperAdminBootstrap.EmailKey] = email;
        if (password is not null) values[NIBRAS.API.Services.SuperAdminBootstrap.PasswordKey] = password;

        return NIBRAS.API.Services.SuperAdminBootstrap.ExecuteAsync(
            Providers(store, logs),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            confirmation,
            isDevelopment: false,
            CancellationToken.None);
    }

    // ---------------------------------------------------------------- checks

    private static async Task ConfigurationRefusalsAsync()
    {
        // Missing or blank credential values: the refuse paths precede any database access.
        foreach (var (email, password, label) in new[]
                 {
                     (null, "value", "a missing email is a configuration refusal"),
                     ("", "value", "a blank email is a configuration refusal"),
                     ("not-an-address", "value", "an invalid email is a configuration refusal"),
                     ("weird@@example.test", "value", "a two-separator email is a configuration refusal"),
                 })
        {
            var store = Store();
            var logs = new CapturedLogs();
            store.Email = email;
            store.Password = password;
            var exit = await InvokeConfiguredAsync(store, logs);
            Program.Check(exit == NIBRAS.API.Services.SuperAdminBootstrap.ExitConfigurationInvalid, label);
            Program.Check(store.Users.Count == 1, "configuration refusal touches no account");
            Program.Check(store.Events.Count == 0, "configuration refusal writes no audit event");
            Program.Check(!logs.Text.ToString().Contains("value", StringComparison.Ordinal),
                "configuration refusal never echoes the credential");
        }

        foreach (var weak in new[]
                 {
                     "short-9a",
                     new string('x', 129),
                     "control\u0007character-password",
                     "utf8-budget-\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D\u4E2D",
                 })
        {
            var store = Store();
            var logs = new CapturedLogs();
            store.Email = OperatorEmail;
            store.Password = weak;
            var exit = await InvokeConfiguredAsync(store, logs);
            Program.Check(exit == NIBRAS.API.Services.SuperAdminBootstrap.ExitConfigurationInvalid,
                "an unsafe password is a configuration refusal");
            Program.Check(store.Users.Count == 1, "unsafe password creates no account");
            Program.Check(!logs.Text.ToString().Contains(weak, StringComparison.Ordinal),
                "unsafe password never echoes the credential");
        }

        {
            // A missing typed confirmation, and a mismatched one: both refuse.
            var store = Store();
            var logs = new CapturedLogs();
            var missing = await InvokeAsync(store, logs, confirmation: null);
            var wrong = await InvokeAsync(store, logs, confirmation: "bystander@example.test");
            Program.Check(missing == NIBRAS.API.Services.SuperAdminBootstrap.ExitConfigurationInvalid,
                "a missing typed confirmation refuses the bootstrap");
            Program.Check(wrong == NIBRAS.API.Services.SuperAdminBootstrap.ExitConfigurationInvalid,
                "a mismatched typed confirmation refuses the bootstrap");
            Program.Check(store.Users.Count == 1 && store.Events.Count == 0,
                "confirmation refusals create no account and no audit event");
        }
    }

    private static async Task SucceedsOnceAsync()
    {
        var store = Store();
        var landlord = Landlord();
        store.Users.Add(landlord);
        var logs = new CapturedLogs();
        var exit = await InvokeAsync(store, logs);
        Program.Check(exit == NIBRAS.API.Services.SuperAdminBootstrap.ExitSuccess,
            "bootstrap creates exactly one SuperAdmin when none exists");
        Program.Check(store.LockedRange, "bootstrap pins the ROLE row with UPDLOCK, HOLDLOCK");
        Program.Check(store.TransactionStarted, "bootstrap runs inside a transaction");
        var created = store.Users.Single(user => user.Email == OperatorEmail);
        Program.Check(created.RoleId == SuperRole().Id && created.IsApproved && !created.IsDeleted,
            "the created account is an approved, active SuperAdmin");
        Program.Check(created.EmailVerified is true && created.EmailVerifiedAt is not null,
            "the created account starts with a verified address epoch");
        Program.Check(!string.IsNullOrWhiteSpace(created.PasswordHash) && created.PasswordHash != "N/A",
            "the created account carries a real password hash");
        Program.Check(store.Users.Single(user => user.Id == 7).PasswordHash == landlord.PasswordHash,
            "bootstrap never touches the other accounts");
        Program.Check(store.Events.Count == 1 && store.Events[0].EventType == UserSecurityEventTypes.SuperAdminBootstrapped,
            "one append-only SuperAdminBootstrapped audit event is committed");
        Program.Check(!logs.Text.ToString().Contains(OperatorPassword, StringComparison.Ordinal),
            "the success path never logs the credential");

        // The mechanism is disabled by construction: the created account blocks every repeat.
        var repeat = await InvokeAsync(store, logs);
        Program.Check(repeat == NIBRAS.API.Services.SuperAdminBootstrap.ExitDisabledSuperAdminExists,
            "a second invocation is disabled once the first SuperAdmin exists");
        Program.Check(store.Users.Count(user => user.Email == OperatorEmail) == 1,
            "the disabled repeat creates no second account");
        Program.Check(store.Events.Count == 1, "the disabled repeat writes no audit event");

        // The placeholder system account in the same database does NOT disable it on its own.
        var placeholderOnly = Store();
        var logsPlaceholder = new CapturedLogs();
        var placeholderExit = await InvokeAsync(placeholderOnly, logsPlaceholder);
        Program.Check(placeholderExit == NIBRAS.API.Services.SuperAdminBootstrap.ExitSuccess,
            "the model placeholder system account does not disable the bootstrap");
        Program.Check(placeholderOnly.Users.Single(u => u.Email == OperatorEmail).RoleId == SuperRole().Id,
            "the bootstrap still creates exactly the first SuperAdmin");
    }

    private static async Task RefusesExistingSuperAdminAsync()
    {
        var store = Store();
        store.Users.Add(new User
        {
            Id = 9,
            RoleId = 4,
            FullName = "Prior Super Admin",
            Email = "prior@example.test",
            Phone = "9999999999",
            PasswordHash = Password("prior-password"),
            IsDeleted = false,
            IsApproved = true,
        });
        var logs = new CapturedLogs();
        var exit = await InvokeAsync(store, logs);
        Program.Check(exit == NIBRAS.API.Services.SuperAdminBootstrap.ExitDisabledSuperAdminExists,
            "an existing login-capable SuperAdmin disables the bootstrap");
        Program.Check(store.Users.Count == 2, "the disabled bootstrap changes no account");
        Program.Check(store.Events.Count == 0, "the disabled bootstrap writes no audit event");
    }

    private static async Task RefusesTakenIdentityAsync()
    {
        var store = Store();
        store.Users.Add(Landlord());
        var logs = new CapturedLogs();
        var exit = await InvokeAsync(store, logs, email: "landlord@example.test", confirmation: "landlord@example.test");
        Program.Check(exit == NIBRAS.API.Services.SuperAdminBootstrap.ExitTargetInUse,
            "a taken target address refuses the bootstrap");
        Program.Check(store.Users.Count == 2, "the refused bootstrap creates no account");
        Program.Check(store.Events.Count == 0, "the refused bootstrap writes no audit event");

        var phoneStore = Store();
        phoneStore.Users.Add(new User
        {
            Id = 11,
            RoleId = 3,
            FullName = "Phone Owner",
            Email = "phone-owner@example.test",
            Phone = "9999999999",
            PasswordHash = Password("phone-owner-password"),
            IsDeleted = false,
            IsApproved = true,
        });
        var logsPhone = new CapturedLogs();
        var phoneExit = await InvokeAsync(phoneStore, logsPhone);
        Program.Check(phoneExit == NIBRAS.API.Services.SuperAdminBootstrap.ExitTargetInUse,
            "the reserved phone in use refuses the bootstrap");
        Program.Check(phoneStore.Users.Count == 2,
            "the refused bootstrap leaves every account untouched");
    }

    private static async Task ConcurrentAttemptsAsync()
    {
        // Two operators start the bootstrap at the same instant. The ROLE row lock
        // serializes them: the winner commits the account and the audit event, the loser
        // re-reads the committed state and fails closed.
        var store = Store();
        var logs = new CapturedLogs();
        var winner = Task.Run(() => InvokeAsync(store, logs));
        var loser = Task.Run(() => InvokeAsync(store, logs));
        var results = await Task.WhenAll(winner, loser);
        Program.Check(results.Count(code => code == NIBRAS.API.Services.SuperAdminBootstrap.ExitSuccess) == 1,
            "exactly one concurrent invocation wins");
        Program.Check(results.Count(code => code == NIBRAS.API.Services.SuperAdminBootstrap.ExitDisabledSuperAdminExists) == 1,
            "the parallel attempt loses against the created SuperAdmin");
        Program.Check(store.Users.Count(user => user.Email == OperatorEmail) == 1,
            "concurrent invocations create exactly one account");
        Program.Check(store.Events.Count == 1, "concurrent invocations commit exactly one audit event");
    }

    private static BootstrapStore Store()
    {
        var store = new BootstrapStore();
        store.Roles.AddRange([new Role { Id = 1, Name = RoleNames.Landlord }, AdminRole(), SuperRole()]);
        // The model-seeded placeholder system account is present in every migrated database.
        store.Users.Add(Placeholder());
        return store;
    }

    // ---------------------------------------------------------------- captured logs

    private sealed class CapturedLogs
    {
        private readonly object gate = new();
        private readonly StringBuilder text = new();

        public string Text { get { lock (gate) return text.ToString(); } }

        public void Append(string line, Exception? exception)
        {
            lock (gate)
            {
                text.AppendLine(line);
                if (exception is not null) text.AppendLine(exception.ToString());
            }
        }
    }

    private sealed class CapturedLoggerFactory(CapturedLogs logs) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => new CapturedLogger(logs);
        public void Dispose() { }
    }

    private sealed class CapturedLogger(CapturedLogs logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            logs.Append(exception is null ? formatter(state, exception) : exception.GetType().Name, exception);
    }

    // ---------------------------------------------------------------- database fake

    /// <summary>
    /// The shared, in-memory target of the fake connections. Its semantics mirror the real
    /// database: the ROLE row lock serializes concurrent bootstrap transactions, and a
    /// waiting competitor reads the post-commit state.
    /// </summary>
    internal sealed class BootstrapStore
    {
        public List<Role> Roles { get; } = [];
        public List<User> Users { get; } = [];
        public List<UserSecurityEvent> Events { get; } = [];
        public int NextUserId = 100;
        public bool LockedRange { get; internal set; }
        public bool TransactionStarted { get; internal set; }

        /// <summary>The configured credential values of the running/next invocation.</summary>
        public string? Email { get; internal set; }
        public string? Password { get; internal set; }

        /// <summary>Serializes whole transactions, emulating the ROLE row lock competitors wait for.</summary>
        internal SemaphoreSlim Serialization { get; } = new(1, 1);
    }

    private sealed class Connection(BootstrapStore store) : DbConnection
    {
        private ConnectionState state;
        [AllowNull] public override string ConnectionString { get; set; } = "Server=offline;Database=bootstrap";
        public override string Database => "bootstrap";
        public override string DataSource => "no-network";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => state;
        public override void Open() => state = ConnectionState.Open;
        public override void Close() => state = ConnectionState.Closed;
        public override void ChangeDatabase(string name) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)
        {
            // Emulate the ROLE row lock: a competitor waits here until the committed
            // transaction releases, then reads the post-commit state.
            store.Serialization.Wait();
            store.TransactionStarted = true;
            return new Transaction(this, store, level);
        }
        protected override DbCommand CreateDbCommand() => new Command(store);
    }

    private sealed class Transaction(DbConnection owner, BootstrapStore store, IsolationLevel level) : DbTransaction
    {
        private int released;
        public override IsolationLevel IsolationLevel => level;
        protected override DbConnection DbConnection => owner;
        public override void Commit() => Release();
        public override void Rollback() => Release();
        protected override void Dispose(bool disposing) { base.Dispose(disposing); Release(); }
        private void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                store.Serialization.Release();
            }
        }
    }

    private sealed class Command(BootstrapStore store) : DbCommand
    {
        private readonly SqlCommand owner = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => owner.Parameters;
        protected override DbParameter CreateDbParameter() => new SqlParameter();
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery() => throw new InvalidOperationException("External SQL prohibited.");
        public override object ExecuteScalar() => throw new InvalidOperationException("Unexpected offline scalar.");
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            Read(CommandText, DbParameterCollection, store);
        protected override void Dispose(bool disposing) { if (disposing) owner.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>
    /// The fake reader honestly understands the exact query shapes the bootstrap issues
    /// and refuses everything else. If the bootstrap ever issues a different shape, these
    /// tests fail loudly instead of silently answering something else.
    /// </summary>
    private static DbDataReader Read(string sql, DbParameterCollection parameters, BootstrapStore store)
    {
        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unexpected offline operation.");
        }

        using var modelDb = new ModelOnlyFactory().CreateDbContext([]);
        var model = modelDb.Model;

        // 1. Existence probes: SELECT CASE WHEN EXISTS (...) THEN ... END.
        if (sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            var existsValue = FirstString(parameters, sql);
            var isMatch = sql.Contains("[EMAIL]", StringComparison.OrdinalIgnoreCase)
                ? store.Users.Any(u => string.Equals(u.Email, existsValue, StringComparison.Ordinal))
                : store.Users.Any(u => string.Equals(u.Phone, existsValue, StringComparison.Ordinal));
            return SingleRow(("c0", typeof(bool)), isMatch);
        }

        // 2. The ROLE pre-check: FROM (SELECT * FROM ROLES WITH (UPDLOCK, HOLDLOCK) ...) AS [n].
        if (!sql.Contains("[USERS]", StringComparison.OrdinalIgnoreCase))
        {
            if (!sql.Contains("ROLES", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unexpected offline table.");
            }

            if (!sql.Contains("UPDLOCK, HOLDLOCK", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The ROLE pre-check lost its locking hint.");
            }

            store.LockedRange = true;
            var role = store.Roles.Single(r => string.Equals(r.Name, FirstString(parameters, sql), StringComparison.Ordinal));
            return Projection(model, sql, [role], (row, _) => row);
        }

        // 3. The USER projection: the disabling pre-check (filtered by role, by deletion
        //    and by the unreachable placeholder hash) or the post-save refetch by id.
        var rows = UserRows(sql, parameters, store);
        return Projection(model, sql, rows, (row, targetType) =>
            targetType == typeof(Role)
                ? store.Roles.First(r => r.Id == ((User)row).RoleId)
                : row);

        static DbDataReader SingleRow((string name, Type type) column, object value)
        {
            var table = new DataTable();
            table.Columns.Add(column.name, column.type);
            table.Rows.Add(value);
            return table.CreateDataReader();
        }

        static IEnumerable<User> UserRows(string sql, DbParameterCollection parameters, BootstrapStore store)
        {
            // The disabling pre-check: filtered by role, by deletion and by the unreachable
            // placeholder hash.
            if (sql.Contains("[ROLE_ID]", StringComparison.OrdinalIgnoreCase) &&
                !sql.Contains("INNER JOIN", StringComparison.OrdinalIgnoreCase))
            {
                var roleId = FirstInt(parameters, sql);
                return store.Users
                    .Where(u => u.RoleId == roleId)
                    .Where(u => !u.IsDeleted)
                    .Where(u => !string.IsNullOrEmpty(u.PasswordHash) && u.PasswordHash != "N/A");
            }

            // The materializer's refetch of one user by id (with the joined role).
            if (sql.Contains("INNER JOIN", StringComparison.OrdinalIgnoreCase))
            {
                var id = FirstInt(parameters, sql);
                return store.Users.Where(u => u.Id == id);
            }

            // Identity existence and equality predicates by value column. A full user
            // projection also contains [EMAIL]/[PHONE] columns, which is why this branch
            // comes last and any unrecognized shape fails loudly instead.
            if (sql.Contains("[EMAIL]", StringComparison.OrdinalIgnoreCase))
            {
                var value = FirstString(parameters, sql);
                return store.Users.Where(u => string.Equals(u.Email, value, StringComparison.Ordinal));
            }

            if (sql.Contains("[PHONE]", StringComparison.OrdinalIgnoreCase))
            {
                var value = FirstString(parameters, sql);
                return store.Users.Where(u => string.Equals(u.Phone, value, StringComparison.Ordinal));
            }

            throw new InvalidOperationException("Unexpected offline USER predicate.");
        }
    }

    /// <summary>Materializes one entity type's projected columns from the EF model.</summary>
    private static DbDataReader Projection(
        IModel model,
        string sql,
        IEnumerable<object> rows,
        Func<object, Type, object> entityOf)
    {
        var rowTypes = rows.Select(row => row.GetType()).Distinct().ToList();
        var aliases = Regex.Matches(sql, @"(?:FROM|JOIN) \[(\w+)\] AS \[(\w+)\]")
            .Cast<Match>()
            .ToDictionary(
                m => m.Groups[2].Value,
                m => model.GetEntityTypes().Single(e => e.GetTableName() == m.Groups[1].Value).ClrType);

        // The projection is everything before the outer FROM: the WHERE clause may embed
        // the same column names.
        var outerFrom = Regex.Match(sql, @"(?m)(?:\A|\s)FROM\s").Index;
        if (outerFrom <= 0)
        {
            throw new InvalidOperationException("Unexpected offline projection.");
        }

        var fields = Regex.Matches(sql[..outerFrom], @"\[(\w+)\]\.\[(\w+)\]");
        if (fields.Count == 0)
        {
            throw new InvalidOperationException("Unexpected offline projection.");
        }

        var properties = fields.Cast<Match>()
            .Select(f =>
            {
                var alias = f.Groups[1].Value;
                var rowType = aliases.TryGetValue(alias, out var mapped)
                    ? mapped
                    : rowTypes.Single();
                return (rowType, property: Property(model, rowType, f.Groups[2].Value));
            })
            .ToList();

        var table = new DataTable();
        foreach (var (_, property) in properties)
        {
            var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            table.Columns.Add("c" + table.Columns.Count, clr);
        }

        foreach (var row in rows)
        {
            table.Rows.Add(properties
                .Select(p =>
                    p.property.PropertyInfo!
                        .GetValue(entityOf(row, p.rowType)) ?? (object)DBNull.Value)
                .ToArray());
        }

        return table.CreateDataReader();
    }

    private static IProperty Property(IModel model, Type sourceType, string columnName)
    {
        var entity = model.FindEntityType(sourceType)!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return entity.GetProperties().Single(p => p.GetColumnName(table) == columnName);
    }

    private static string FirstString(DbParameterCollection parameters, string sql)
    {
        // Parameterized predicate, then the inlined literal form.
        var fromParameters = parameters.Cast<DbParameter>()
            .Where(p => p.Value is string)
            .Select(p => (string)p.Value!)
            .ToList();
        if (fromParameters.Count == 1)
        {
            return fromParameters[0];
        }

        var literal = Regex.Match(sql, @"(?:<>|<|=) N?'([^']*)'").Groups[1].Value;
        if (literal.Length == 0)
        {
            throw new InvalidOperationException("Unexpected offline predicate.");
        }

        return literal;
    }

    private static int? FirstInt(DbParameterCollection parameters, string sql)
    {
        var fromParameters = parameters.Cast<DbParameter>()
            .Where(p => p.Value is int)
            .Select(p => (int)p.Value!)
            .ToList();
        if (fromParameters.Count == 1)
        {
            return fromParameters[0];
        }

        var literal = Regex.Match(sql, @"\[ROLE_ID\] = (\d+)").Groups[1].Value;
        return literal.Length == 0 ? throw new InvalidOperationException("Unexpected offline predicate.") : int.Parse(literal);
    }

    // ---------------------------------------------------------------- save changes

    private sealed class Save(BootstrapStore store) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data,
            InterceptionResult<int> result,
            CancellationToken token = default)
        {
            foreach (var entry in data.Context!.ChangeTracker.Entries().ToList())
            {
                if (entry.State == EntityState.Added && entry.Entity is User user)
                {
                    store.NextUserId++;
                    entry.Property(nameof(User.Id)).CurrentValue = store.NextUserId;
                    entry.Property(nameof(User.Id)).IsTemporary = false;
                    store.Users.Add(user);
                }
                else if (entry.State == EntityState.Added && entry.Entity is UserSecurityEvent securityEvent)
                {
                    entry.Property(nameof(UserSecurityEvent.Id)).CurrentValue = store.Events.Count + 1;
                    entry.Property(nameof(UserSecurityEvent.Id)).IsTemporary = false;
                    store.Events.Add(securityEvent);
                }
                else if (entry.State is not (EntityState.Unchanged or EntityState.Detached))
                {
                    throw new InvalidOperationException("Unexpected offline write.");
                }

                entry.State = EntityState.Unchanged;
            }

            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }
}

/// <summary>The process-argument recognition of the bootstrap invocation.</summary>
public static class SuperAdminInvocationChecks
{
    public static void Run()
    {
        Program.Check(
            !NIBRAS.API.Services.SuperAdminBootstrap.IsInvocation(["--help"], out var unrelated)
            && unrelated is null,
            "bootstrap invocation is not inferred from unrelated arguments");
        Program.Check(
            NIBRAS.API.Services.SuperAdminBootstrap.IsInvocation(
                ["--bootstrap-superadmin", "--confirm", "operator@example.test"], out var confirmation)
            && confirmation == "operator@example.test",
            "bootstrap invocation carries the typed confirmation");
        Program.Check(
            NIBRAS.API.Services.SuperAdminBootstrap.IsInvocation(
                ["--bootstrap-superadmin"], out var absent)
            && absent is null,
            "bootstrap invocation without a confirmation stays an invocation");
    }
}

/// <summary>
/// The bootstrap credentials may only be carried by the bootstrap invocation itself; a
/// normal production-like start must refuse to boot with them.
/// </summary>
public static class GuardChecks
{
    public static void Run()
    {
        var environment = new TestEnvironment { EnvironmentName = Environments.Production };

        var withBootstrapKeys = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "api.example.test",
            [NIBRAS.API.Services.SuperAdminBootstrap.EmailKey] = "operator@example.test",
            [NIBRAS.API.Services.SuperAdminBootstrap.PasswordKey] = "unused-in-this-test",
        }).Build();

        var withoutBootstrapKeys = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "api.example.test",
        }).Build();

        var refused = false;
        try
        {
            NIBRAS.API.Services.ProductionConfigurationGuard.Validate(environment, withBootstrapKeys);
        }
        catch (InvalidOperationException)
        {
            refused = true;
        }

        Program.Check(refused, "a normal start refuses to boot with bootstrap credentials");

        NIBRAS.API.Services.ProductionConfigurationGuard.Validate(
            environment, withBootstrapKeys, bootstrapInvocation: true);
        Program.Check(true, "the bootstrap invocation is the only start that carries bootstrap credentials");

        NIBRAS.API.Services.ProductionConfigurationGuard.Validate(
            environment, withoutBootstrapKeys, bootstrapInvocation: false);
        Program.Check(true, "a normal start without bootstrap credentials stays permitted");

        var development = new TestEnvironment { EnvironmentName = Environments.Development };
        NIBRAS.API.Services.ProductionConfigurationGuard.Validate(
            development, withBootstrapKeys, bootstrapInvocation: false);
        Program.Check(true, "Development keeps its permissive startup behavior");
    }
}
