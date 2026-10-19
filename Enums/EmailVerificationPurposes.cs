using NIBRAS.Models;

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

    /// <summary>
    /// The role an account must hold for the <see cref="AdminActivation"/> purpose to be
    /// issued. The purpose is reserved for an Admin account created by a SuperAdmin and is
    /// never issued for a Landlord, an Investor or a SuperAdmin account.
    /// </summary>
    public const string AdminActivationRequiredRole = RoleNames.Admin;

    /// <summary>
    /// Whether the role may hold a <see cref="Registration"/> token. The registration
    /// verification is reserved for a new Landlord or Investor account: an administrator
    /// account has its own activation flow, which also sets the password, and a registration
    /// token would mark it verified without that password setup.
    /// </summary>
    public static bool IsRegistrationRole(string? roleName) =>
        roleName is RoleNames.Landlord or RoleNames.Investor;
}
