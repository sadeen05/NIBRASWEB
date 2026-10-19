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
    /// soft-deleted account) and as the pending address of another account.
    /// </summary>
    Task<EmailVerificationResult> IssueEmailChangeTokenAsync(
        int userId,
        string newEmail,
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
}
