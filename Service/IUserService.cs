using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IUserService
{
    Task<UserListResponse> GetAllAsync(int pageNumber, int pageSize, string? role, bool? isApproved);
    Task<UserDto?> GetByIdAsync(int id);
    Task<UserApprovalStatusDto?> GetApprovalStatusAsync(int id);
    Task<UserCreationResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<bool> ApproveAsync(int id, int approvedById);
    Task<bool> RejectApprovalAsync(int id, RejectInvestorApprovalRequest request);
    Task<bool> UpdateAsync(int id, UpdateUserRequest request);
    Task<bool> DeleteAsync(int id);
}
