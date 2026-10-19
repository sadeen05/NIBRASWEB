namespace NibrasWeb.Enums;

/// <summary>
/// Security audit event types stored in <c>USER_SECURITY_EVENTS.EVENT_TYPE</c>. The list is
/// closed on purpose: an event type that is not listed here is not a security event.
/// </summary>
public static class UserSecurityEventTypes
{
    /// <summary>A self-registration created an account that still has to verify its address.</summary>
    public const string RegistrationCreated = "RegistrationCreated";

    /// <summary>
    /// A SuperAdmin created a Landlord or an Investor account that still has to verify its
    /// address.
    /// </summary>
    public const string AdminCreatedUnverified = "AdminCreatedUnverified";

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

    /// <summary>
    /// A verification token was created for an account. The purpose is recorded in the event
    /// details; the token itself is never part of an event.
    /// </summary>
    public const string VerificationTokenIssued = "VerificationTokenIssued";

    /// <summary>
    /// The live tokens of an account were retired, for example because the address of an
    /// unverified account was corrected, so a link sent to the previous address can no longer
    /// confirm anything. The reason is recorded in the event details.
    /// </summary>
    public const string VerificationTokenInvalidated = "VerificationTokenInvalidated";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        RegistrationCreated,
        AdminCreatedUnverified,
        AdminAccountCreated,
        VerificationEmailSent,
        VerificationEmailResent,
        VerificationEmailDeliveryFailed,
        EmailVerified,
        VerificationFailed,
        LoginBlockedUnverified,
        EmailChangeRequested,
        EmailChanged,
        AdminActivated,
        AdminPasswordSet,
        AccountApproved,
        VerificationTokenIssued,
        VerificationTokenInvalidated,
    };

    /// <summary>
    /// Whether the value belongs to the closed set of event types. An unknown type is a
    /// programming error and is refused, so no free-form value ever reaches the audit trail.
    /// </summary>
    public static bool IsKnown(string? eventType) => eventType is not null && Known.Contains(eventType);
}
