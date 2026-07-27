namespace NIBRAS.API.DTOs;

public record UpdateUserRequest
(
    string FullName,
    string Email,
    string Phone,
    string Role
);
