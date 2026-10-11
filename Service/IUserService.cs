using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IUserService
{
    Task<UserListResponse> GetAllAsync(int pageNumber, int pageSize, string? role, bool? isApproved);
    Task<UserDto?> GetByIdAsync(int id);
    Task<UserDto> CreateAsync(CreateUserRequest request);
    Task<bool> ApproveAsync(int id, int approvedById);
    Task<bool> UpdateAsync(int id, UpdateUserRequest request);
    Task<bool> DeleteAsync(int id);
}
