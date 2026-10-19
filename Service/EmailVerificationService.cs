using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

/// <summary>
/// Issues and consumes the email verification tokens.
/// <para>
/// <b>Serialisation.</b> Every issuance locks the account row (UPDLOCK, HOLDLOCK) before it
/// invalidates the earlier tokens and inserts the new one, so two concurrent issuances for
/// one account cannot interleave and leave two live tokens for the same purpose. The
/// invalidation and the insertion share the transaction, so a failed issuance leaves the
/// previous, still usable token untouched.
/// </para>
/// <para>
/// <b>Consumption.</b> The token is read to learn the account, the account row is then locked
/// (the same order the issuance uses, so the two cannot deadlock), and the consumption itself
/// is one conditional database update: exactly one caller can win the row. The consumed
/// token, the account state change and the security audit event are committed in one
/// transaction, so a failure anywhere rolls all of it back.
/// </para>
/// <para>
/// The plaintext token never reaches the database: only its SHA-256 hash is stored, and the
/// plaintext is handed back once, in process, to the caller that has to deliver it.
/// </para>
/// </summary>
public sealed class EmailVerificationService : IEmailVerificationService
{
    private readonly NebrasdbContext _context;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(
        NebrasdbContext context,
        IOptions<EmailOptions> options,
        ILogger<EmailVerificationService> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailVerificationResult> IssueRegistrationTokenAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        await IssueInTransactionAsync(userId, EmailVerificationPurposes.Registration, cancellationToken);

    public async Task<EmailVerificationResult> IssueAdminActivationTokenAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        await IssueInTransactionAsync(userId, EmailVerificationPurposes.AdminActivation, cancellationToken);

    public async Task<EmailVerificationResult> IssueEmailChangeTokenAsync(
        int userId,
        string newEmail,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        var address = newEmail?.Trim();
        if (string.IsNullOrWhiteSpace(address) ||
            address.Length > 255 ||
            address.Any(character => character < ' '))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.InvalidAddress, "malformed_address");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var result = await IssueEmailChangeCoreAsync(userId, address, currentPassword, cancellationToken);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        return result;
    }

    public async Task<EmailVerificationResult> ConsumeTokenAsync(
        string token,
        string expectedPurpose,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.InvalidToken, "empty_token");
        }

        if (!IsKnownPurpose(expectedPurpose))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.WrongPurpose, "unknown_purpose");
        }

        var now = DateTime.UtcNow;
        var hash = VerificationTokenCodec.HashToken(token.Trim());

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // The token is read without a lock first, only to learn which account it belongs to.
        var row = await _context.EmailVerificationTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.InvalidToken, "unknown_token");
        }

        if (!string.Equals(row.Purpose, expectedPurpose, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.WrongPurpose, "purpose_mismatch");
        }

        if (row.ConsumedAt is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.AlreadyUsed, "already_consumed");
        }

        if (row.ExpiresAt <= now)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.Expired, "expired");
        }

        // The account row is locked before the token row, the same order the issuance uses, so
        // the two paths cannot deadlock against each other.
        if (!await LockAccountAsync(row.UserId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.UserNotFound, "user_missing");
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == row.UserId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.UserNotFound, "user_deleted_or_missing");
        }

        // An activation token may only be completed together with the password setup, so a
        // plain address verification never consumes it and never leaves the account verified
        // without a password. The role is re-checked here as well, so a token that was issued
        // while the account was an Admin is not honoured after the role changed.
        if (row.Purpose == EmailVerificationPurposes.AdminActivation)
        {
            if (!string.Equals(
                    user.Role.Name,
                    EmailVerificationPurposes.AdminActivationRequiredRole,
                    StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogWarning(
                    "Refused an admin activation token for account {UserId}: the account does not hold the {RequiredRole} role.",
                    user.Id,
                    EmailVerificationPurposes.AdminActivationRequiredRole);
                return EmailVerificationResult.Failed(VerificationOutcome.RoleNotAllowed, "role_not_allowed");
            }

            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(
                VerificationOutcome.OnboardingRequired, "admin_activation_requires_the_password_setup");
        }

        // A second live token for the same account and purpose would mean that a newer
        // request superseded this one. Such a token is stale and is refused.
        var otherLiveTokens = await _context.EmailVerificationTokens
            .AsNoTracking()
            .CountAsync(
                t => t.UserId == row.UserId &&
                     t.Purpose == row.Purpose &&
                     t.Id != row.Id &&
                     t.ConsumedAt == null &&
                     t.ExpiresAt > now,
                cancellationToken);
        if (otherLiveTokens > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.AlreadyUsed, "superseded_request");
        }

        // The atomic consumption: the conditional update lets exactly one caller win the row.
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [EMAIL_VERIFICATION_TOKENS] SET [CONSUMED_AT] = {now} WHERE [ID] = {row.Id} AND [CONSUMED_AT] IS NULL AND [EXPIRES_AT] > {now}",
            cancellationToken);
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.AlreadyUsed, "consumed_concurrently");
        }

        var appliedAt = DateTime.UtcNow;
        var isAddressChange = row.Purpose == EmailVerificationPurposes.EmailChange;

        if (isAddressChange)
        {
            // The pending address must still be exactly the address this token was issued for.
            // If it is missing or has been replaced by another request, the token is stale and
            // nothing is applied - a token can only ever confirm the address it is bound to.
            if (string.IsNullOrWhiteSpace(user.PendingEmail))
            {
                await transaction.RollbackAsync(cancellationToken);
                return EmailVerificationResult.Failed(VerificationOutcome.InvalidToken, "no_pending_address");
            }

            if (string.IsNullOrWhiteSpace(row.PreviousEmail) ||
                !string.Equals(user.PendingEmail, row.PreviousEmail, StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);
                return EmailVerificationResult.Failed(VerificationOutcome.InvalidToken, "stale_target_address");
            }

            user.Email = user.PendingEmail;
            user.PendingEmail = null;
        }

        user.EmailVerified = true;
        user.EmailVerifiedAt = appliedAt;

        _context.UserSecurityEvents.Add(new UserSecurityEvent
        {
            UserId = user.Id,
            EventType = isAddressChange ? UserSecurityEventTypes.EmailChanged : UserSecurityEventTypes.EmailVerified,
            Details = isAddressChange ? "address_replaced" : null,
            CreatedAt = appliedAt,
        });

        try
        {
            // One commit for the consumed token, the account state and the audit event.
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The address was taken by another account in the meantime. Nothing is applied: the
            // transaction is rolled back and the caller is told the address is in use.
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                "Address verification for account {UserId} was refused: the new address is already in use.",
                user.Id);
            return EmailVerificationResult.Failed(VerificationOutcome.EmailInUse, "address_already_active");
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Address verification completed for account {UserId} ({Purpose}).",
            user.Id,
            row.Purpose);

        return new EmailVerificationResult(VerificationOutcome.Succeeded);
    }

    public async Task<EmailVerificationResult> ResendTokenAsync(
        int userId,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        if (!IsKnownPurpose(purpose))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.WrongPurpose, "unknown_purpose");
        }

        var now = DateTime.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // The account lock is taken before the limits are read, so two parallel resends cannot
        // both pass the cooldown or the daily cap.
        if (!await LockAccountAsync(userId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.UserNotFound, "user_missing");
        }

        // The limits count the account, not the purpose: a resend must not be able to multiply
        // the mail volume of one account by using several purposes.
        var lastCreated = await _context.EmailVerificationTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .MaxAsync(t => (DateTime?)t.CreatedAt, cancellationToken);
        if (lastCreated is { } last && (now - last).TotalSeconds < _options.ResendCooldownSeconds)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.CooldownActive, "cooldown_active");
        }

        // Every issuance attempt counts, including the tokens that were later invalidated: the
        // rows are never deleted, so the cap cannot be bypassed by resending.
        var sentToday = await _context.EmailVerificationTokens
            .AsNoTracking()
            .CountAsync(t => t.UserId == userId && t.CreatedAt >= now.Date, cancellationToken);
        if (sentToday >= _options.ResendDailyCap)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.DailyCapReached, "daily_cap_reached");
        }

        var result = await IssueCoreAsync(userId, purpose, cancellationToken);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        return result;
    }

    public async Task InvalidateLiveTokensAsync(
        int userId,
        string? purpose = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (purpose is null)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [EMAIL_VERIFICATION_TOKENS] SET [CONSUMED_AT] = {now} WHERE [USER_ID] = {userId} AND [CONSUMED_AT] IS NULL",
                cancellationToken);
        }
        else
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [EMAIL_VERIFICATION_TOKENS] SET [CONSUMED_AT] = {now} WHERE [USER_ID] = {userId} AND [PURPOSE] = {purpose} AND [CONSUMED_AT] IS NULL",
                cancellationToken);
        }

        _context.UserSecurityEvents.Add(new UserSecurityEvent
        {
            UserId = userId,
            EventType = UserSecurityEventTypes.VerificationTokenInvalidated,
            Details = reason,
            CreatedAt = now,
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<EmailVerificationResult> IssueInTransactionAsync(
        int userId,
        string purpose,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var result = await IssueCoreAsync(userId, purpose, cancellationToken);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        return result;
    }

    private async Task<EmailVerificationResult> IssueEmailChangeCoreAsync(
        int userId,
        string address,
        string currentPassword,
        CancellationToken cancellationToken)
    {
        // The account row is locked first, so two concurrent address requests for one account
        // are serialised.
        if (!await LockAccountAsync(userId, cancellationToken))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.UserNotFound, "user_missing");
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return EmailVerificationResult.Failed(VerificationOutcome.UserNotFound, "user_deleted_or_missing");
        }

        if (string.Equals(user.Email, address, StringComparison.OrdinalIgnoreCase))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.AddressUnchanged, "already_the_active_address");
        }

        // The current password must be confirmed before a new address may be claimed: a
        // hijacked session alone must not be able to move the address of an account. An account
        // whose stored hash is unusable can never confirm a password, so it cannot change its
        // address either.
        if (!PasswordVerifier.TryVerify(currentPassword, user.PasswordHash))
        {
            _logger.LogWarning("Refused an address change for account {UserId}: the current password was not confirmed.", userId);
            return EmailVerificationResult.Failed(
                VerificationOutcome.InvalidCurrentPassword, "current_password_required");
        }

        // The new address must be free: as the active address (also of a soft-deleted account,
        // which still owns the address) and as the pending address of another account. The rows
        // are read under an update lock, so two accounts cannot claim the same address at the
        // same time and both pass the check. The comparison uses the database collation, which
        // is case-insensitive, exactly like the unique index on USERS.EMAIL.
        var conflictingIds = await _context.Users
            .FromSqlInterpolated(
                $"SELECT * FROM [USERS] WITH (UPDLOCK, HOLDLOCK) WHERE [EMAIL] = {address} OR [PENDING_EMAIL] = {address}")
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        if (conflictingIds.Any(id => id != userId))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.EmailInUse, "address_already_claimed");
        }

        // The active address is only replaced by a successful verification of the new one.
        user.PendingEmail = address;

        return await IssueCoreAsync(userId, EmailVerificationPurposes.EmailChange, cancellationToken);
    }

    /// <summary>
    /// Creates the token, invalidates the earlier tokens of the same purpose, and records the
    /// audit event. The caller owns the transaction, so a failure here rolls the invalidation
    /// back and leaves the previous token usable.
    /// </summary>
    private async Task<EmailVerificationResult> IssueCoreAsync(
        int userId,
        string purpose,
        CancellationToken cancellationToken)
    {
        if (!IsKnownPurpose(purpose))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.WrongPurpose, "unknown_purpose");
        }

        // The account row is locked before anything else, so concurrent issuances for one
        // account are serialised and cannot leave two live tokens for the same purpose.
        if (!await LockAccountAsync(userId, cancellationToken))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.UserNotFound, "user_missing");
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return EmailVerificationResult.Failed(VerificationOutcome.UserNotFound, "user_deleted_or_missing");
        }

        // The activation purpose is reserved for an Admin account that a SuperAdmin created.
        if (purpose == EmailVerificationPurposes.AdminActivation &&
            !string.Equals(
                user.Role.Name,
                EmailVerificationPurposes.AdminActivationRequiredRole,
                StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Refused an admin activation token for account {UserId}: the account does not hold the {RequiredRole} role.",
                userId,
                EmailVerificationPurposes.AdminActivationRequiredRole);
            return EmailVerificationResult.Failed(VerificationOutcome.RoleNotAllowed, "role_not_allowed");
        }

        var now = DateTime.UtcNow;

        // A new token invalidates the earlier ones of the same purpose, so only the newest
        // link for that purpose works.
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [EMAIL_VERIFICATION_TOKENS] SET [CONSUMED_AT] = {now} WHERE [USER_ID] = {userId} AND [PURPOSE] = {purpose} AND [CONSUMED_AT] IS NULL",
            cancellationToken);

        var plaintext = VerificationTokenCodec.CreateToken();
        var expiresAt = now.AddHours(GetTtlHours(purpose));

        _context.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = VerificationTokenCodec.HashToken(plaintext),
            // For an address change the token is bound to the requested address, so the
            // consumption can prove that the recipient proved exactly that address.
            PreviousEmail = purpose == EmailVerificationPurposes.EmailChange ? user.PendingEmail : null,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        });

        _context.UserSecurityEvents.Add(new UserSecurityEvent
        {
            UserId = userId,
            EventType = UserSecurityEventTypes.VerificationTokenIssued,
            Details = purpose,
            CreatedAt = now,
        });

        await _context.SaveChangesAsync(cancellationToken);

        return EmailVerificationResult.Success(new IssuedVerificationToken(plaintext, expiresAt));
    }

    /// <summary>
    /// Locks the account row for the current transaction. The lock is taken on the primary key
    /// (an index seek, not a scan) and is held until the transaction ends.
    /// </summary>
    public async Task<bool> LockAccountAsync(int userId, CancellationToken cancellationToken = default)
    {
        var lockedId = await _context.Users
            .FromSqlInterpolated($"SELECT * FROM [USERS] WITH (UPDLOCK, HOLDLOCK) WHERE [ID] = {userId}")
            .AsNoTracking()
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return lockedId != 0;
    }

    private int GetTtlHours(string purpose) => purpose switch
    {
        EmailVerificationPurposes.Registration => _options.RegistrationTokenTtlHours,
        EmailVerificationPurposes.EmailChange => _options.EmailChangeTokenTtlHours,
        EmailVerificationPurposes.AdminActivation => _options.AdminActivationTokenTtlHours,
        _ => _options.RegistrationTokenTtlHours,
    };

    private static bool IsKnownPurpose(string? purpose) =>
        purpose is EmailVerificationPurposes.Registration
            or EmailVerificationPurposes.EmailChange
            or EmailVerificationPurposes.AdminActivation;

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
}
