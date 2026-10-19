namespace NIBRAS.API.Services;

/// <summary>The outcome of a registration verification delivery.</summary>
public enum RegistrationVerificationOutcome
{
    /// <summary>The verification mail was handed to the mailer.</summary>
    Sent,

    /// <summary>
    /// The mail could not be delivered. The account stays unverified; a new mail can be
    /// requested, which retires the failed one.
    /// </summary>
    DeliveryFailed,

    /// <summary>
    /// The request was throttled: the resend cooldown has not elapsed yet, or the daily cap
    /// for the account is reached.
    /// </summary>
    Throttled,

    /// <summary>
    /// The account cannot receive a verification mail: it does not exist, it is deleted, it
    /// does not hold a Landlord or an Investor role, or it is not explicitly unverified (a
    /// legacy account with a null verification flag counts as verified).
    /// </summary>
    AccountNotEligible,
}

/// <summary>The result of a registration verification delivery. It never carries the token.</summary>
public sealed record RegistrationVerificationResult(
    RegistrationVerificationOutcome Outcome,
    string? FailureReason = null)
{
    public bool Sent => Outcome == RegistrationVerificationOutcome.Sent;
}

/// <summary>
/// Delivers the registration verification mail of a new Landlord or Investor account. The
/// plaintext token stays inside this service: it is handed to the mailer in process, and it is
/// never returned to the caller, never logged and never stored.
/// </summary>
/// <remarks>
/// An anonymous caller must map <b>every</b> outcome to the same generic answer. The cooldown
/// and the daily cap are account specific, so a different answer for a throttled request would
/// reveal whether the account exists and whether it is still unverified.
/// </remarks>
public interface IRegistrationVerificationService
{
    /// <summary>
    /// Issues (or reissues) the registration verification of an account and delivers it.
    /// Issuing a new verification retires the earlier ones in the same transaction, so only
    /// the newest link works.
    /// </summary>
    /// <param name="userId">The account to verify.</param>
    /// <param name="actorId">The acting user, when somebody else caused the delivery.</param>
    /// <param name="ip">The remote address of the request, for the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<RegistrationVerificationResult> SendAsync(
        int userId,
        int? actorId = null,
        string? ip = null,
        CancellationToken cancellationToken = default);
}
