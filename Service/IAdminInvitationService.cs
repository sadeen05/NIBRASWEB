namespace NIBRAS.API.Services;

/// <summary>The outcome of an admin invitation delivery.</summary>
public enum AdminInvitationOutcome
{
    /// <summary>The invitation was handed to the mailer.</summary>
    Sent,

    /// <summary>
    /// The mail could not be delivered. The account stays unverified and unusable; a new
    /// invitation can be issued, which retires the failed one.
    /// </summary>
    DeliveryFailed,

    /// <summary>
    /// The account cannot receive an invitation: it is not an active administrator account,
    /// or it has already been activated.
    /// </summary>
    AccountNotEligible,
}

/// <summary>The result of an admin invitation delivery. It never carries the token.</summary>
public sealed record AdminInvitationResult(AdminInvitationOutcome Outcome, string? FailureReason = null)
{
    public bool Sent => Outcome == AdminInvitationOutcome.Sent;
}

/// <summary>
/// Delivers the activation invitation of a new administrator account. The plaintext token
/// stays inside this service: it is handed to the mailer in process, and it is never returned
/// to the caller, never logged and never stored.
/// </summary>
public interface IAdminInvitationService
{
    /// <summary>
    /// Issues (or reissues) the activation invitation of an administrator account and
    /// delivers it. Issuing a new invitation retires the earlier ones in the same
    /// transaction, so only the newest link works.
    /// </summary>
    /// <param name="adminUserId">The administrator account to invite.</param>
    /// <param name="actorId">The super administrator who asked for the invitation.</param>
    /// <param name="ip">The remote address of the request, for the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminInvitationResult> SendAsync(
        int adminUserId,
        int? actorId = null,
        string? ip = null,
        CancellationToken cancellationToken = default);
}
