using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public record CreateUserRequest
([param: Required]
    [param: MaxLength(100)]
    string FullName,

    [param: Required]
    [param: EmailAddress]
    [param: MaxLength(255)]
    string Email,

    [param: Required]
    [param: MaxLength(20)]
    string Phone,

    [param: Required]
    [param: MinLength(10)]
    string Password,

    [param: Required]
    [param: Range(1, 3)]
    int RoleId
);