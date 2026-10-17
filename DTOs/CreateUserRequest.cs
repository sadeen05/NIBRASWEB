using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public record CreateUserRequest
([param: Required, NoControlCharacters]
    [param: MaxLength(100)]
    string FullName,

    [param: Required, NoControlCharacters]
    [param: EmailAddress]
    [param: MaxLength(255)]
    string Email,

    [param: Required, NoControlCharacters]
    [param: MaxLength(20)]
    string Phone,

    [param: Required, MinLength(10), MaxLength(128), NoControlCharacters, MaxUtf8Bytes(72)]
    string Password,

    [param: Required]
    [param: Range(1, 3)]
    int RoleId,

    [param: MaxLength(200), NoControlCharacters]
    string? CompanyName = null,

    [param: MaxLength(100), NoControlCharacters]
    string? CompanyRegistrationNumber = null
);