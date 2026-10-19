using System.Text.Json.Serialization;

namespace NIBRAS.API.Services;

/// <summary>The outcome of a verification operation. No HTTP concern leaks into the service.</summary>
public enum VerificationOutcome
{
    /// <summary>The token was issued or consumed as requested.</summary>
    Succeeded,

    /// <summary>The token is unknown (no row with that hash).</summary>
    InvalidToken,

    /// <summary>The token existed but its lifetime has ended.</summary>
    Expired,

    /// <summary>The token was already consumed or invalidated by a later one.</summary>
    AlreadyUsed,

    /// <summary>The token was issued for another purpose than the one requested.</summary>
    WrongPurpose,

    /// <summary>The account does not exist (or was deleted).</summary>
    UserNotFound,

    /// <summary>The account does not hold the role the purpose requires.</summary>
    RoleNotAllowed,

    /// <summary>The address is already in use, as an active or as a pending address.</summary>
    EmailInUse,

    /// <summary>The address is already the active address of the account.</summary>
    AddressUnchanged,

    /// <summary>The address is not a usable one.</summary>
    InvalidAddress,

    /// <summary>
    /// The account has already been activated. An invitation can never reset the password of
    /// an account that is already in use.
    /// </summary>
    AlreadyActivated,

    /// <summary>
    /// The current password was not confirmed. A new address may only be claimed with the
    /// current password of the account, so a hijacked session alone cannot move it.
    /// </summary>
    InvalidCurrentPassword,

    /// <summary>The resend cooldown has not elapsed yet.</summary>
    CooldownActive,

    /// <summary>The daily resend cap for the account is reached.</summary>
    DailyCapReached,

    /// <summary>
    /// The token belongs to the admin activation flow, which can only be completed together
    /// with the password setup. A plain address verification must never consume it.
    /// </summary>
    OnboardingRequired,
}

/// <summary>
/// A freshly issued token. The plaintext is exposed once, to the caller that has to deliver
/// it; <see cref="ToString"/> redacts it so it cannot reach a log by accident.
/// </summary>
public sealed class IssuedVerificationToken
{
    public IssuedVerificationToken(string plaintext, DateTime expiresAt)
    {
        Plaintext = plaintext;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// The token to put into the activation link. It is never stored or logged, and it is
    /// excluded from serialization: the value is handed to the delivery step in process only,
    /// so it can never end up in an HTTP response by accident.
    /// </summary>
    [JsonIgnore]
    public string Plaintext { get; }

    /// <summary>When the token stops being usable.</summary>
    public DateTime ExpiresAt { get; }

    public override string ToString() => "[verification token redacted]";
}

/// <summary>The result of a verification operation.</summary>
public sealed record EmailVerificationResult(
    VerificationOutcome Outcome,
    [property: JsonIgnore] IssuedVerificationToken? Issued = null,
    string? FailureReason = null)
{
    public bool Succeeded => Outcome == VerificationOutcome.Succeeded;

    public static EmailVerificationResult Success(IssuedVerificationToken issued) =>
        new(VerificationOutcome.Succeeded, issued);

    public static EmailVerificationResult Failed(VerificationOutcome outcome, string? reason = null) =>
        new(outcome, null, reason);
}
