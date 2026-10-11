namespace NIBRAS.API.DTOs;

public record UserDto
(
    int Id,
    string FullName,
    string Email,
    string Phone,
    string Role,
    DateTime CreatedAt,
    bool IsApproved,
    string? CompanyName,
    string? CompanyRegistrationNumber
);

public record UserListResponse(
    IReadOnlyList<UserDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages);
