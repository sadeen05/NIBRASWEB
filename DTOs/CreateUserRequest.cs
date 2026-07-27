using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public record CreateUserRequest
([property: Required]
    [property: MaxLength(100)]
    string FullName,

    [property: Required]
    [property: EmailAddress]
    [property: MaxLength(255)]
    string Email,

    [property: Required]
    [property: MaxLength(20)]
    string Phone,

    [property: Required]
    [property: Range(1, 3)]
    int RoleId
);