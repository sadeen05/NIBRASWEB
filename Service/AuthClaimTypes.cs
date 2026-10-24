namespace NIBRAS.API.Services;

/// <summary>
/// Custom JWT claims that bind a session to the security state it was issued for. The standard
/// claims identify the account; these claims make an old session stop working when the account's
/// address or verification epoch changes.
/// </summary>
public static class AuthClaimTypes
{
    /// <summary>
    /// The UTC tick value of <c>User.EmailVerifiedAt</c> at the moment the session was issued.
    /// A confirmed address change updates that value, so every session issued before the change
    /// fails validation instead of keeping access to the old identity.
    /// </summary>
    public const string EmailVerifiedAt = "email_verified_at";
}
