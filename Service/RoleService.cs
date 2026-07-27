using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class RoleService : IRoleService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<RoleService> _logger;

    public RoleService(NebrasdbContext context, ILogger<RoleService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<RoleDto>> GetAllAsync()
    {
        var roles = await _context.Roles.ToListAsync();
        var result = new List<RoleDto>();
        foreach (var role in roles)
            result.Add(role.Adapt<RoleDto>());
        return result;
    }

    public async Task<RoleDto?> GetByIdAsync(int id)
    {
        var role = await _context.Roles.FindAsync(id);
        if (role == null) return null;
        return role.Adapt<RoleDto>();
    }

    public async Task<RoleDto> CreateAsync(CreateRoleRequest request)
    {
        var role = request.Adapt<Role>();
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();
        return role.Adapt<RoleDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateRoleRequest request)
    {
        var role = await _context.Roles.FindAsync(id);
        if (role == null) return false;

        role.Name = request.Name;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var role = await _context.Roles.FindAsync(id);
        if (role == null) return false;
        _context.Roles.Remove(role);
        await _context.SaveChangesAsync();
        return true;
    }
}
