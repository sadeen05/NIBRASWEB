namespace NIBRAS.API.DTOs;

public record UserDto
(
    int Id,
    string FullName,
    string Email,
    string Phone,
    string Role,
    DateTime CreatedAt
);
