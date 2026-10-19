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
    bool IsApproved);

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
