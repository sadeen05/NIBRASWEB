using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IRoleService
{
    Task<List<RoleDto>> GetAllAsync();
    Task<RoleDto?> GetByIdAsync(int id);
    Task<RoleDto> CreateAsync(CreateRoleRequest request);
    Task<bool> UpdateAsync(int id, UpdateRoleRequest request);
    Task<bool> DeleteAsync(int id);
}
