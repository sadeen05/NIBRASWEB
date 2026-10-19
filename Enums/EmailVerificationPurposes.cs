namespace NibrasWeb.Enums;

/// <summary>
/// The purpose a verification token was issued for. Stored verbatim in
/// <c>EMAIL_VERIFICATION_TOKENS.PURPOSE</c>.
/// </summary>
public static class EmailVerificationPurposes
{
    /// <summary>
    /// A newly registered account must prove its address before the account can be used.
    /// </summary>
    public const string Registration = "Registration";

    /// <summary>
    /// A request to replace the active address. The active address is only replaced by a
    /// successful verification of the new one.
    /// </summary>
    public const string EmailChange = "EmailChange";

    /// <summary>
    /// A new Admin created by a SuperAdmin. One single-use link both proves the address and
    /// lets the new Admin set their own password, so no password is ever chosen by somebody
    /// else and no activation can be skipped.
    /// </summary>
    public const string AdminActivation = "AdminActivation";
}
