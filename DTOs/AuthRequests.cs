using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public record RegisterRequest(
    [param: Required, MaxLength(100)] string FullName,
    [param: Required, EmailAddress, MaxLength(255)] string Email,
    [param: Required, MaxLength(20)] string Phone,
    [param: Required, MinLength(10)] string Password,
    [param: Range(1, 2)] int RoleId,
    string? CompanyName,
    string? CompanyRegistrationNumber);

public record LoginRequest(
    [param: Required, EmailAddress] string Email,
    [param: Required] string Password);

public record AuthUserSummary(
    int Id,
    string FullName,
    string Role,
    bool IsApproved);

public record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    AuthUserSummary User);
