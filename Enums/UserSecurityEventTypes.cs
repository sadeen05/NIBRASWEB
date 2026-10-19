namespace NibrasWeb.Enums;

/// <summary>
/// Security audit event types stored in <c>USER_SECURITY_EVENTS.EVENT_TYPE</c>. The list is
/// closed on purpose: an event type that is not listed here is not a security event.
/// </summary>
public static class UserSecurityEventTypes
{
    /// <summary>A self-registration created an account that still has to verify its address.</summary>
    public const string RegistrationCreated = "RegistrationCreated";

    /// <summary>A SuperAdmin created an Admin account that still has to activate.</summary>
    public const string AdminAccountCreated = "AdminAccountCreated";

    /// <summary>A verification mail was handed to the mailer.</summary>
    public const string VerificationEmailSent = "VerificationEmailSent";

    /// <summary>A verification mail was requested again (resend).</summary>
    public const string VerificationEmailResent = "VerificationEmailResent";

    /// <summary>The mailer could not deliver the verification mail.</summary>
    public const string VerificationEmailDeliveryFailed = "VerificationEmailDeliveryFailed";

    /// <summary>An address was verified (and, for a change, the active address was replaced).</summary>
    public const string EmailVerified = "EmailVerified";

    /// <summary>A verification attempt was refused: unknown, expired or already used token.</summary>
    public const string VerificationFailed = "VerificationFailed";

    /// <summary>A login was refused because the account has not verified its address yet.</summary>
    public const string LoginBlockedUnverified = "LoginBlockedUnverified";

    /// <summary>An address change was requested; the active address is unchanged until it is verified.</summary>
    public const string EmailChangeRequested = "EmailChangeRequested";

    /// <summary>The active address was replaced after a successful verification.</summary>
    public const string EmailChanged = "EmailChanged";

    /// <summary>A new Admin completed the activation link.</summary>
    public const string AdminActivated = "AdminActivated";

    /// <summary>A new Admin set their own password through the activation link.</summary>
    public const string AdminPasswordSet = "AdminPasswordSet";

    /// <summary>An admin approved an account (the investor approval, independent of verification).</summary>
    public const string AccountApproved = "AccountApproved";
}
