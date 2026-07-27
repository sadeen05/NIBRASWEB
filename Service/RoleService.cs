using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class RoleService : IRoleService
{
    private readonly IRepository<Role> _roleRepository;
    private readonly ILogger<RoleService> _logger;

    public RoleService(IRepository<Role> roleRepository, ILogger<RoleService> logger)
    {
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<List<RoleDto>> GetAllAsync()
    {
        var roles = await _roleRepository.GetAllAsync();
        var result = new List<RoleDto>();

        foreach (var role in roles)
        {
            result.Add(role.Adapt<RoleDto>());
        }

        return result;
    }

    public async Task<RoleDto?> GetByIdAsync(int id)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        if (role == null) return null;
        return role.Adapt<RoleDto>();
    }

    public async Task<RoleDto> CreateAsync(CreateRoleRequest request)
    {
        var role = request.Adapt<Role>();
        await _roleRepository.AddAsync(role);
        await _roleRepository.SaveChangesAsync();
        return role.Adapt<RoleDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateRoleRequest request)
    {
        var role = await _roleRepository.GetByIdAsync(id);
        if (role == null) return false;

        role.Name = request.Name;

        await _roleRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var result = await _roleRepository.DeleteAsync(id);
        if (result == false) return false;
        await _roleRepository.SaveChangesAsync();
        return true;
    }
}
