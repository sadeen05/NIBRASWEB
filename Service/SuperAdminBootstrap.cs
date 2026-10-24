using Microsoft.EntityFrameworkCore;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

/// <summary>
/// The one-time, explicitly invoked out-of-band provisioning of the first SuperAdmin.
/// This is never a startup path and never an API route: the host only enters this mode
/// when the process is started by an operator with <see cref="InvocationArgument"/>, the
/// host never listens for requests in that mode, and no background service is started.
/// The Development-only seeder (<see cref="SuperAdminSeeder"/>) remains the Development
/// provisioning path; the bootstrap invocation refuses to run in Development altogether.
///
/// Safety properties:
/// - the credentials are supplied only through configuration (environment variables or the
///   deployment secret store) and are never printed, logged or returned; outside
///   Development a normal serving start that still carries the bootstrap credentials is
///   refused by <see cref="ProductionConfigurationGuard"/>;
/// - the operator repeats the exact target email as a typed confirmation so an accidental
///   run cannot create a privileged account in a forgotten target;
/// - the bootstrap is disabled once a login-capable SuperAdmin exists: the pre-check runs
///   again inside the transaction, and a concurrent invocation loses the race against the
///   ROLE row lock and fails closed instead of creating a second privileged account;
/// - the account and its audit event are committed in one transaction, so the
///   <c>USERS</c> unique indexes on EMAIL and PHONE remain the last resort against a
///   duplicate account;
/// - the password rules match the registration rules (minimum length, the supported UTF-8
///   byte length and no control characters) and are hashed with BCrypt work factor 12,
///   exactly like every other account.
/// </summary>
public static class SuperAdminBootstrap
{
    /// <summary>The process argument that selects the one-time bootstrap mode.</summary>
    public const string InvocationArgument = "--bootstrap-superadmin";

    /// <summary>
    /// The process argument that carries the typed confirmation. Its value must repeat the
    /// exact target email; the password can never be part of the invocation.
    /// </summary>
    public const string ConfirmationArgument = "--confirm";

    public const string EmailKey = "Bootstrap:SuperAdminEmail";
    public const string PasswordKey = "Bootstrap:SuperAdminPassword";

    /// <summary>
    /// The unreachable placeholder hash of the model-seeded system account
    /// (system@nibras.internal). It can never authenticate anybody, so it never counts as
    /// an existing SuperAdmin and never disables the bootstrap.
    /// </summary>
    private const string PlaceholderHash = "N/A";

    // Process exit codes, so the operator (and deployment automation) can distinguish the
    // outcomes without parsing log text.
    public const int ExitSuccess = 0;
    public const int ExitFailed = 1;
    public const int ExitConfigurationInvalid = 2;
    public const int ExitDisabledSuperAdminExists = 3;
    public const int ExitTargetInUse = 4;
    public const int ExitDevelopmentRefused = 5;

    /// <summary>
    /// Runs the one-time bootstrap. Returns one of the Exit* codes and writes no secret.
    /// </summary>
    /// <param name="services">The host's built service provider (never a listening host).</param>
    /// <param name="configuration">The resolved application configuration.</param>
    /// <param name="confirmation">The operator's typed confirmation (the exact target email).</param>
    /// <param name="isDevelopment">Whether the selected environment is Development.</param>
    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        IConfiguration configuration,
        string? confirmation,
        bool isDevelopment,
        CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SuperAdminBootstrap");

        // The Development provisioning path is the seed configuration; the maintenance
        // command is refused there so two different mechanisms can never both create the
        // first SuperAdmin.
        if (isDevelopment)
        {
            logger.LogWarning(
                "SuperAdmin bootstrap refused: Development uses the Development-only seed configuration instead. " +
                "Run the maintenance command against the explicit deployment environment.");
            return ExitDevelopmentRefused;
        }

        var email = (configuration[EmailKey] ?? string.Empty).Trim();
        var password = configuration[PasswordKey] ?? string.Empty;

        if (email.Length == 0 || !LooksLikeEmail(email) || password.Length == 0)
        {
            // The refusal names the keys but never echoes their values.
            logger.LogCritical(
                "SuperAdmin bootstrap refused: {Message} is missing, blank or not a valid address. " +
                "Credentials are supplied only through {EmailKey} and {PasswordKey} and are never printed.",
                "required configuration",
                EmailKey,
                PasswordKey);
            return ExitConfigurationInvalid;
        }

        // The confirmation must repeat the exact target address, so an accidental invocation
        // against a wrong environment cannot complete.
        if (string.IsNullOrWhiteSpace(confirmation) ||
            !string.Equals(confirmation.Trim(), email, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogCritical(
                "SuperAdmin bootstrap refused: the {Argument} value must repeat the exact target address.",
                ConfirmationArgument);
            return ExitConfigurationInvalid;
        }

        // The same password rules as self-registration: registration allows 10-128 characters
        // with the supported UTF-8 byte length and no control characters.
        if (password.Length < 10 || password.Length > 128 ||
            PasswordPolicy.GetUtf8ByteCount(password) > 72 || password.Any(char.IsControl))
        {
            logger.LogCritical(
                "SuperAdmin bootstrap refused: the configured password does not satisfy the " +
                "registration password rules (length and byte budget). The value is never echoed.");
            return ExitConfigurationInvalid;
        }

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NebrasdbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IUserSecurityEventService>();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The SuperAdmin ROLE row is pinned exactly as the land, offer and contract
        // mutations pin their rows (UPDLOCK, HOLDLOCK): two concurrent bootstrap
        // invocations are serialized at the database, and the loser re-reads the state
        // committed by the winner before it decides anything.
        var superAdminRole = await context.Roles
            .FromSqlInterpolated($"SELECT * FROM ROLES WITH (UPDLOCK, HOLDLOCK) WHERE NAME = {RoleNames.SuperAdmin}")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The SuperAdmin role does not exist in the target database; the model may not be migrated there.");

        // The bootstrap is disabled once a login-capable SuperAdmin exists. The model's
        // placeholder system account (an unreachable hash) never counts: it cannot
        // authenticate anybody, so it must not stop the very first real administrator.
        var existingSuperAdmins = await context.Users
            .IgnoreQueryFilters()
            .Where(user => user.RoleId == superAdminRole.Id &&
                           !user.IsDeleted &&
                           user.PasswordHash != null &&
                           user.PasswordHash != PlaceholderHash)
            .ToListAsync(cancellationToken);

        if (existingSuperAdmins.Count > 0)
        {
            logger.LogCritical(
                "SuperAdmin bootstrap refused: the target database already holds an active SuperAdmin account, "
                + "so the mechanism is disabled. No account was created and nothing was changed.");
            return ExitDisabledSuperAdminExists;
        }

        // The target address and the reserved phone must both be free. The email and phone
        // unique indexes stay the last resort; these pre-checks produce a clear outcome
        // instead of a raw constraint error.
        var emailInUse = await context.Users
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Email == email, cancellationToken);
        var phoneInUse = await context.Users
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Phone == SuperAdminSeeder.ReservedPhone, cancellationToken);

        if (emailInUse || phoneInUse)
        {
            logger.LogCritical(
                "SuperAdmin bootstrap refused: the target address or the reserved phone of the SuperAdmin " +
                "identity is already used by another account. No account was created; the bootstrap " +
                "never reuses or changes an existing account.");
            return ExitTargetInUse;
        }

        var now = DateTime.UtcNow;
        var superAdmin = new User
        {
            RoleId = superAdminRole.Id,
            FullName = "Super Admin",
            Email = email,
            Phone = SuperAdminSeeder.ReservedPhone,
            // Work factor 12, exactly like every other account hash.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            IsDeleted = false,
            IsApproved = true,
            ApprovedAt = now,
            // The address is proven at birth, so the first activation can never depend on
            // an email delivery to the operator's secret.
            EmailVerified = true,
            EmailVerifiedAt = now,
        };

        try
        {
            context.Users.Add(superAdmin);
            await context.SaveChangesAsync(cancellationToken);

            // The audit event is written in the same transaction: the account and its
            // audit trail exist together or not at all, and no credential or hash is part
            // of the event.
            await audit.RecordAsync(
                superAdmin.Id,
                UserSecurityEventTypes.SuperAdminBootstrapped,
                superAdmin.Id,
                null,
                "bootstrap",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // The implicit rollback of the transaction removes any partially written state.
            // A unique violation means another writer created the identity concurrently;
            // the bootstrap loses that race and refuses.
            var uniqueConflict = exception is DbUpdateException update && DatabaseErrors.IsUniqueViolation(update);
            logger.LogCritical(
                exception,
                "SuperAdmin bootstrap failed: the account was not created. Reason category: {Category}.",
                uniqueConflict ? "unique_conflict" : "database_error");
            return uniqueConflict ? ExitTargetInUse : ExitFailed;
        }

        // The mechanism is now disabled by construction: every later invocation sees the
        // login-capable SuperAdmin and refuses.
        logger.LogInformation(
            "SuperAdmin bootstrap completed for {Email}. The mechanism is now disabled: any further " +
            "invocation for this database will refuse. Protect and remove the bootstrap configuration " +
            "after this run.",
            email);
        return ExitSuccess;
    }

    /// <summary>
    /// Minimal address sanity: a local part, one separator, a domain. It is a refusal guard
    /// for a mistyped configuration, not an address validator: the unique index and the
    /// operator's confirmation are the real guarantees.
    /// </summary>
    private static bool LooksLikeEmail(string value) =>
        value.Length <= 320 &&
        !value.Any(char.IsControl) &&
        !value.Any(char.IsWhiteSpace) &&
        value.Count(character => character == '@') == 1 &&
        !value.StartsWith('@') &&
        !value.EndsWith('@') &&
        !value.Contains("@.");

    /// <summary>
    /// Extracts the typed confirmation from the invocation arguments, or returns
    /// <c>null</c> when the process was not started as a bootstrap invocation at all.
    /// An invocation without a usable confirmation stays a bootstrap invocation (with a
    /// <c>null</c> confirmation) and is refused by <see cref="ExecuteAsync"/>.
    /// </summary>
    public static bool IsInvocation(string[] args, out string? confirmation)
    {
        var invocation = Array.IndexOf(args, InvocationArgument);
        if (invocation < 0)
        {
            confirmation = null;
            return false;
        }

        var confirmIndex = Array.IndexOf(args, ConfirmationArgument);
        confirmation = confirmIndex >= 0 && confirmIndex + 1 < args.Length
            ? args[confirmIndex + 1]
            : null;
        return true;
    }
}
