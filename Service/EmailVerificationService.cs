using System.ComponentModel.DataAnnotations;
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
    private readonly TimeProvider _timeProvider;

    public EmailVerificationService(
        NebrasdbContext context,
        IOptions<EmailOptions> options,
        ILogger<EmailVerificationService> logger,
        TimeProvider timeProvider)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<EmailVerificationResult> IssueRegistrationTokenAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        await IssueInTransactionAsync(userId, EmailVerificationPurposes.Registration, null, cancellationToken);

    public async Task<EmailVerificationResult> RedeemAdminActivationAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.InvalidToken, "empty_token");
        }

        // The password of the new Admin is their own: it is validated with the existing policy
        // (at least 10 characters, at most 128, at most 72 UTF-8 bytes) and it is never
        // returned, logged or stored in clear text.
        if (string.IsNullOrWhiteSpace(newPassword) ||
            newPassword.Length < 10 ||
            newPassword.Length > 128)
        {
            throw new ValidationFailedException("The password must be between 10 and 128 characters.");
        }

        PasswordPolicy.Validate(newPassword);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var hash = VerificationTokenCodec.HashToken(token.Trim());

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var row = await _context.EmailVerificationTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.InvalidToken, "unknown_token");
        }

        // Only an activation token may activate an account, and only once.
        if (!string.Equals(row.Purpose, EmailVerificationPurposes.AdminActivation, StringComparison.Ordinal))
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

        // The account row is locked before the token row, the same order every other path uses.
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

        // The purpose is restricted to an Admin account, and the account must still be active.
        if (!string.Equals(
                user.Role.Name,
                EmailVerificationPurposes.AdminActivationRequiredRole,
                StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                "Refused an admin activation for account {UserId}: the account does not hold the {RequiredRole} role.",
                user.Id,
                EmailVerificationPurposes.AdminActivationRequiredRole);
            return EmailVerificationResult.Failed(VerificationOutcome.RoleNotAllowed, "role_not_allowed");
        }

        // An account that is already verified has been activated before, and a legacy account
        // (EMAIL_VERIFIED IS NULL) counts as verified as well: an invitation can never reset the
        // password of an account that is in use, and it can never take over a legacy account.
        if (user.EmailVerified is not false)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.AlreadyActivated, "not_activatable");
        }

        // A newer invitation invalidates the older one, so a superseded link is refused.
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
            return EmailVerificationResult.Failed(VerificationOutcome.AlreadyUsed, "superseded_invitation");
        }

        // The atomic consumption: exactly one caller can win the row, so a double click or two
        // parallel redemptions cannot both activate the account.
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [EMAIL_VERIFICATION_TOKENS] SET [CONSUMED_AT] = {now} WHERE [ID] = {row.Id} AND [CONSUMED_AT] IS NULL AND [EXPIRES_AT] > {now}",
            cancellationToken);
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.AlreadyUsed, "consumed_concurrently");
        }

        var appliedAt = _timeProvider.GetUtcNow().UtcDateTime;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.EmailVerified = true;
        user.EmailVerifiedAt = appliedAt;

        _context.UserSecurityEvents.Add(new UserSecurityEvent
        {
            UserId = user.Id,
            EventType = UserSecurityEventTypes.AdminActivated,
            CreatedAt = appliedAt,
        });
        _context.UserSecurityEvents.Add(new UserSecurityEvent
        {
            UserId = user.Id,
            EventType = UserSecurityEventTypes.AdminPasswordSet,
            CreatedAt = appliedAt,
        });

        try
        {
            // One commit for the consumed token, the new password, the verified address and the
            // audit events.
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return EmailVerificationResult.Failed(VerificationOutcome.EmailInUse, "address_already_active");
        }

        await transaction.CommitAsync(cancellationToken);

        // The account, the role and the token are never part of the log line.
        _logger.LogInformation("Admin account {UserId} activated.", user.Id);

        return new EmailVerificationResult(VerificationOutcome.Succeeded);
    }

    public async Task<EmailVerificationResult> ReissueAdminInvitationAsync(
        int adminUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == adminUserId && !u.IsDeleted, cancellationToken);
        if (user is null)
        {
            return EmailVerificationResult.Failed(VerificationOutcome.UserNotFound, "user_missing");
        }

        if (!string.Equals(
                user.Role.Name,
                EmailVerificationPurposes.AdminActivationRequiredRole,
                StringComparison.Ordinal))
        {
            return EmailVerificationResult.Failed(VerificationOutcome.RoleNotAllowed, "role_not_allowed");
        }

        // Only an account that is explicitly unverified may receive an invitation. A legacy
        // account (EMAIL_VERIFIED IS NULL) counts as verified, and so does an activated account.
        if (user.EmailVerified is not false)
        {
            return EmailVerificationResult.Failed(VerificationOutcome.AlreadyActivated, "not_activatable");
        }

        // Issuing a new invitation retires the older ones in the same transaction, so only the
        // newest link works. The retirement and its audit event are written together with the new
        // token, so the trail can never disagree with the tokens.
        return await IssueInTransactionAsync(
            adminUserId,
            EmailVerificationPurposes.AdminActivation,
            "invitation_reissued",
            cancellationToken);
    }

    public async Task<EmailVerificationResult> IssueAdminActivationTokenAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        await IssueInTransactionAsync(userId, EmailVerificationPurposes.AdminActivation, null, cancellationToken);

    public async Task<EmailVerificationResult> IssueEmailChangeTokenAsync(
        int userId,
        string newEmail,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        var address = newEmail?.Trim();
        if (string.IsNullOrWhiteSpace(address) ||
            address.Length > 255 ||
            address.Any(character => character < ' ') ||
            !new EmailAddressAttribute().IsValid(address))
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

        var now = _timeProvider.GetUtcNow().UtcDateTime;
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

        // The role is re-checked here as well, the same way the admin activation flow does it:
        // a registration token that was issued while the account was a Landlord or an Investor
        // is not honoured after the role changed.
        if (row.Purpose == EmailVerificationPurposes.Registration &&
            !EmailVerificationPurposes.IsRegistrationRole(user.Role.Name))
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                "Refused a registration verification for account {UserId}: the account does not hold a Landlord or Investor role.",
                user.Id);
            return EmailVerificationResult.Failed(VerificationOutcome.RoleNotAllowed, "role_not_allowed");
        }

        // A registration token proves exactly the address it was issued for. When the address
        // of the account has changed since - an administrative correction, or an address change
        // that was completed in the meantime - the token is stale and confirms nothing: it can
        // never mark a corrected address as verified.
        if (row.Purpose == EmailVerificationPurposes.Registration &&
            (string.IsNullOrWhiteSpace(row.PreviousEmail) ||
             !string.Equals(user.Email, row.PreviousEmail, StringComparison.OrdinalIgnoreCase)))
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                "Refused a registration verification for account {UserId}: the address of the account has changed since the token was issued.",
                user.Id);
            return EmailVerificationResult.Failed(VerificationOutcome.InvalidToken, "stale_target_address");
        }

        // The token was issued for an account that still had to prove its address, so it may
        // only perform that transition. An account that is verified in the meantime - and a
        // legacy account, whose null flag counts as verified - is left exactly as it is, and a
        // legacy account is never changed by a verification token.
        if (row.Purpose == EmailVerificationPurposes.Registration && user.EmailVerified is not false)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogWarning(
                "Refused a registration verification for account {UserId}: the account is not an unverified account.",
                user.Id);
            return EmailVerificationResult.Failed(
                VerificationOutcome.NotVerificationEligible,
                "not_verification_eligible");
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

        var appliedAt = _timeProvider.GetUtcNow().UtcDateTime;
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

        var now = _timeProvider.GetUtcNow().UtcDateTime;

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

        var result = await IssueCoreAsync(userId, purpose, null, cancellationToken);
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

        var now = _timeProvider.GetUtcNow().UtcDateTime;

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
        string? invalidationReason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var result = await IssueCoreAsync(userId, purpose, invalidationReason, cancellationToken);
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

        // An address change sends a mail to the requested address, so it is subject to the same
        // per-account cooldown and daily cap as a resend. The checks run under the account lock
        // taken above, so two parallel requests cannot both pass them. They count every
        // verification token of the account, not just the address-change tokens, so the mail
        // volume of one account cannot be multiplied by switching purposes.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var lastCreated = await _context.EmailVerificationTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .MaxAsync(t => (DateTime?)t.CreatedAt, cancellationToken);
        if (lastCreated is { } last && (now - last).TotalSeconds < _options.ResendCooldownSeconds)
        {
            return EmailVerificationResult.Failed(VerificationOutcome.CooldownActive, "cooldown_active");
        }

        var sentToday = await _context.EmailVerificationTokens
            .AsNoTracking()
            .CountAsync(t => t.UserId == userId && t.CreatedAt >= now.Date, cancellationToken);
        if (sentToday >= _options.ResendDailyCap)
        {
            return EmailVerificationResult.Failed(VerificationOutcome.DailyCapReached, "daily_cap_reached");
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

        // The requested address is deliberately not part of the event: the audit trail records
        // that a change was requested, never the address or a token.
        _context.UserSecurityEvents.Add(new UserSecurityEvent
        {
            UserId = userId,
            EventType = UserSecurityEventTypes.EmailChangeRequested,
            Details = "pending_address_set",
            CreatedAt = now,
        });

        return await IssueCoreAsync(userId, EmailVerificationPurposes.EmailChange, null, cancellationToken);
    }

    /// <summary>
    /// Creates the token, invalidates the earlier tokens of the same purpose, and records the
    /// audit event. The caller owns the transaction, so a failure here rolls the invalidation
    /// back and leaves the previous token usable.
    /// </summary>
    private async Task<EmailVerificationResult> IssueCoreAsync(
        int userId,
        string purpose,
        string? invalidationReason,
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

        // Only an account that is explicitly unverified may be activated. A legacy account
        // (EMAIL_VERIFIED IS NULL) counts as verified, and so does an activated account: an
        // invitation may therefore never set the password of an account that is already in use,
        // and it can never take over a legacy account.
        if (purpose == EmailVerificationPurposes.AdminActivation && user.EmailVerified is not false)
        {
            _logger.LogWarning(
                "Refused an admin activation token for account {UserId}: the account is not an unactivated account.",
                userId);
            return EmailVerificationResult.Failed(VerificationOutcome.AlreadyActivated, "not_activatable");
        }

        // The registration verification is reserved for a new Landlord or Investor account. An
        // administrator account has its own activation flow, which also sets the password; a
        // registration token would mark it verified without that password setup, and would
        // then block its activation, because an activation token is only issued for an account
        // that is not verified yet.
        if (purpose == EmailVerificationPurposes.Registration &&
            !EmailVerificationPurposes.IsRegistrationRole(user.Role.Name))
        {
            _logger.LogWarning(
                "Refused a registration verification token for account {UserId}: the account does not hold a Landlord or Investor role.",
                userId);
            return EmailVerificationResult.Failed(VerificationOutcome.RoleNotAllowed, "role_not_allowed");
        }

        // Only an account that is explicitly unverified may receive a registration
        // verification. A legacy account (EMAIL_VERIFIED IS NULL) counts as verified, and so
        // does a verified account: a legacy account therefore keeps its state and can never
        // be pushed into the unverified state by a verification mail. The account has to be
        // created unverified before its first verification token is issued.
        if (purpose == EmailVerificationPurposes.Registration && user.EmailVerified is not false)
        {
            _logger.LogWarning(
                "Refused a registration verification token for account {UserId}: the account is not an unverified account.",
                userId);
            return EmailVerificationResult.Failed(
                VerificationOutcome.NotVerificationEligible,
                "not_verification_eligible");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // A new token invalidates the earlier ones of the same purpose, so only the newest
        // link for that purpose works. The retirement and its audit event are written in the
        // same transaction as the new token, so the trail can never disagree with the tokens.
        var retiredTokens = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [EMAIL_VERIFICATION_TOKENS] SET [CONSUMED_AT] = {now} WHERE [USER_ID] = {userId} AND [PURPOSE] = {purpose} AND [CONSUMED_AT] IS NULL",
            cancellationToken);
        if (retiredTokens > 0)
        {
            _context.UserSecurityEvents.Add(new UserSecurityEvent
            {
                UserId = userId,
                EventType = UserSecurityEventTypes.VerificationTokenInvalidated,
                Details = invalidationReason ?? purpose,
                CreatedAt = now,
            });
        }

        var plaintext = VerificationTokenCodec.CreateToken();
        var expiresAt = now.AddHours(GetTtlHours(purpose));

        _context.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = VerificationTokenCodec.HashToken(plaintext),
            // The address the token is bound to. For an address change it is the requested
            // address the recipient has to prove, so the consumption can prove that the
            // recipient proved exactly that address. For a registration it is the address the
            // token was issued for, so a token that outlives an address change can never
            // confirm a different address. For the other purposes it is null.
            PreviousEmail = purpose switch
            {
                EmailVerificationPurposes.EmailChange => user.PendingEmail,
                EmailVerificationPurposes.Registration => user.Email,
                _ => null,
            },
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

    /// <summary>Retires an interrupted issuance under the account lock, preserving newer requests and audit.</summary>
    public async Task CancelDeliveryAsync(int userId, string plaintext, string purpose, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockAccountAsync(userId, cancellationToken))
            throw new InvalidOperationException("Cancelled delivery account is unavailable.");
        var hash = VerificationTokenCodec.HashToken(plaintext);
        var token = await _context.EmailVerificationTokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.UserId == userId && t.TokenHash == hash && t.Purpose == purpose, cancellationToken);
        if (token is null) throw new InvalidOperationException("Cancelled delivery token is unavailable.");
        if (token.ConsumedAt is not null) { await transaction.CommitAsync(cancellationToken); return; }
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [EMAIL_VERIFICATION_TOKENS] SET [CONSUMED_AT] = {now} WHERE [ID] = {token.Id} AND [CONSUMED_AT] IS NULL", cancellationToken);
        var user = await _context.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        await _context.Entry(user).ReloadAsync(cancellationToken);
        if (purpose == EmailVerificationPurposes.EmailChange &&
            string.Equals(user.PendingEmail, token.PreviousEmail, StringComparison.OrdinalIgnoreCase))
            user.PendingEmail = null;
        _context.UserSecurityEvents.Add(new UserSecurityEvent {
            UserId = userId, EventType = UserSecurityEventTypes.VerificationTokenInvalidated,
            Details = "delivery_cancelled:" + purpose, CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
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
