using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public record RegisterRequest(
    [param: Required, MaxLength(100), NoControlCharacters] string FullName,
    [param: Required, EmailAddress, MaxLength(255), NoControlCharacters] string Email,
    [param: Required, MaxLength(20), NoControlCharacters] string Phone,
    [param: Required, MinLength(10), MaxLength(128), NoControlCharacters, MaxUtf8Bytes(72)] string Password,
    [param: Range(1, 2)] int RoleId,
    [param: MaxLength(200), NoControlCharacters] string? CompanyName,
    [param: MaxLength(100), NoControlCharacters] string? CompanyRegistrationNumber);

public record LoginRequest(
    [param: Required, EmailAddress, MaxLength(255), NoControlCharacters] string Email,
    [param: Required, MaxLength(128), NoControlCharacters, MaxUtf8Bytes(72)] string Password);

public record AuthUserSummary(
    int Id,
    string FullName,
    string Role,
    bool IsApproved,
    string Verification = VerificationStatuses.NotApplicable,
    string? VerificationMessage = null,
    string? Email = null,
    bool? EmailVerified = null,
    string? PendingEmail = null);

/// <summary>
/// The status of a verification that was handed to the mailer. The values are part of the API
/// contract: the client shows its confirmation step for <see cref="HandedOver"/> and for
/// <see cref="Failed"/>, because both mean that the account has to confirm its address.
/// </summary>
public static class VerificationStatuses
{
    /// <summary>No verification applies to the account.</summary>
    public const string NotApplicable = "not_applicable";

    /// <summary>
    /// The mail was handed to the mailer. It does **not** mean that the message was received:
    /// the server never sees the inbox of the recipient.
    /// </summary>
    public const string HandedOver = "handed_over";

    /// <summary>
    /// The mail could not be handed over. The account stays unverified and unusable, and a new
    /// verification can be requested - which retires the failed one.
    /// </summary>
    public const string Failed = "failed";
}

public record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    AuthUserSummary User);

/// <summary>
/// Activation of a new Admin account: the single-use invitation token together with the
/// password the new Admin chooses for themselves.
/// </summary>
public record ActivateAdminRequest(
    [param: Required, MaxLength(200), NoControlCharacters]
    string Token,

    [param: Required, MinLength(10), MaxLength(128), NoControlCharacters, MaxUtf8Bytes(72)]
    string Password);

/// <summary>
/// Confirmation of the address of a new Landlord or Investor account: the single-use
/// verification token from the verification mail. Only a registration token is accepted.
/// </summary>
public record VerifyEmailRequest(
    [param: Required, MaxLength(200), NoControlCharacters]
    string Token);

/// <summary>
/// A request for a new verification mail. The address only serves to find the account; the
/// answer of the endpoint never depends on whether the address belongs to one.
/// </summary>
public record ResendVerificationRequest(
    [param: Required, EmailAddress, MaxLength(255), NoControlCharacters]
    string Email);

/// <summary>
/// Request to replace the active address of an authenticated account. The current password is
/// required so a hijacked session alone cannot move the address; the active address is only
/// replaced after the new address has been verified.
/// </summary>
public record ChangeEmailRequest(
    [param: Required, EmailAddress, MaxLength(255), NoControlCharacters]
    string NewEmail,

    [param: Required, MaxLength(128), NoControlCharacters, MaxUtf8Bytes(72)]
    string CurrentPassword);
