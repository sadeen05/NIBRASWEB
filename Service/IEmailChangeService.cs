namespace NIBRAS.API.Services;

/// <summary>The outcome of an authenticated address-change request.</summary>
public enum EmailChangeOutcome
{
    /// <summary>The confirmation mail was handed to the mailer.</summary>
    Sent,

    /// <summary>The current password was not confirmed.</summary>
    InvalidCurrentPassword,

    /// <summary>The requested address is already the active address.</summary>
    AddressUnchanged,

    /// <summary>The requested address is already in use by another account.</summary>
    EmailInUse,

    /// <summary>The requested address is not usable.</summary>
    InvalidAddress,

    /// <summary>The account is missing or is not allowed to change its address.</summary>
    AccountNotEligible,

    /// <summary>The per-account cooldown or daily mail cap is active.</summary>
    Throttled,

    /// <summary>The confirmation mail could not be handed to the mailer.</summary>
    DeliveryFailed,
}

/// <summary>The result of an address-change request. It never carries the token.</summary>
public sealed record EmailChangeResult(EmailChangeOutcome Outcome, string? FailureReason = null)
{
    public bool Sent => Outcome == EmailChangeOutcome.Sent;
}

/// <summary>
/// Issues and delivers the confirmation mail for an authenticated address change. The active
/// address is never changed here: it only changes when the new address is confirmed through
/// the single-use token endpoint.
/// </summary>
public interface IEmailChangeService
{
    /// <param name="userId">The authenticated account that asks for the change.</param>
    /// <param name="newEmail">The address to prove before it replaces the active one.</param>
    /// <param name="currentPassword">The current password, to prove the account holder.</param>
    /// <param name="ip">The remote address, for the append-only audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EmailChangeResult> RequestAsync(
        int userId,
        string newEmail,
        string currentPassword,
        string? ip = null,
        CancellationToken cancellationToken = default);
}
