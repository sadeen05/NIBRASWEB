namespace NIBRAS.API.Services;

/// <summary>
/// Issues and consumes the email verification tokens. Every token is bound to one account and
/// one purpose, expires, can be used exactly once, and is invalidated when a later token of
/// the same purpose is issued. The state change and the security audit event of a consumption
/// are committed in one transaction, and the consumption itself is a single conditional
/// database update, so two concurrent uses of the same token can never both succeed.
/// </summary>
public interface IEmailVerificationService
{
    /// <summary>
    /// Issues a registration token for an account that has to prove its address before the
    /// account can be used.
    /// </summary>
    Task<EmailVerificationResult> IssueRegistrationTokenAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a token that replaces the active address of the account once the new address is
    /// verified. The address must be free everywhere: as an active address (also of a
    /// soft-deleted account) and as the pending address of another account. The current
    /// password of the account must be confirmed, so a hijacked session alone cannot move the
    /// address.
    /// </summary>
    Task<EmailVerificationResult> IssueEmailChangeTokenAsync(
        int userId,
        string newEmail,
        string currentPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues an activation token for an Admin account that a SuperAdmin created. The purpose
    /// is refused for an account that does not hold the Admin role.
    /// </summary>
    Task<EmailVerificationResult> IssueAdminActivationTokenAsync(
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a token for the given purpose. A token issued for another purpose is refused
    /// and left usable for its own purpose; an admin activation token is never consumed here,
    /// because it may only be completed together with the password setup.
    /// </summary>
    Task<EmailVerificationResult> ConsumeTokenAsync(
        string token,
        string expectedPurpose,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a fresh token for the same purpose and invalidates the earlier ones. The
    /// resend cooldown and the daily cap are enforced here.
    /// </summary>
    Task<EmailVerificationResult> ResendTokenAsync(
        int userId,
        string purpose,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires the live tokens of an account, optionally only those of one purpose. It is used
    /// when something invalidates a link - for example when the address of an unverified
    /// account is corrected, so that a link sent to the previous address can no longer confirm
    /// anything.
    /// </summary>
    Task InvalidateLiveTokensAsync(
        int userId,
        string? purpose = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks the account row for the current transaction and reports whether the account
    /// exists. A caller that changes the account takes this lock <b>before</b> it touches the
    /// verification tokens, so every path locks the account before the token table and the two
    /// cannot deadlock against each other.
    /// </summary>
    Task<bool> LockAccountAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems an admin activation invitation: the single-use token together with the
    /// password the new Admin chooses. The token is consumed, the password hash is set, the
    /// address is marked verified and the audit events are appended - all in one transaction,
    /// so a concurrent redemption has exactly one winner and a failure changes nothing.
    /// </summary>
    Task<EmailVerificationResult> RedeemAdminActivationAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a fresh activation invitation for a new Admin account and invalidates the older
    /// ones, so only the newest link works. The plaintext token is returned to the caller that
    /// delivers the invitation; it is never logged and never stored.
    /// </summary>
    Task<EmailVerificationResult> ReissueAdminInvitationAsync(
        int adminUserId,
        CancellationToken cancellationToken = default);
}
