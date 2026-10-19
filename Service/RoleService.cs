using Mapster;
using Microsoft.Data.SqlClient;
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

        // A role that is still assigned to users cannot be deleted: USERS.ROLE_ID references it and
        // the foreign key rejects the delete, which must answer 409 instead of an unexpected 500.
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            throw new BusinessRuleException("Cannot delete a role that is assigned to users.");
        }

        return true;
    }

    // SQL Server reports a foreign-key violation as error 547; USERS is the only table that
    // references ROLES.
    private static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number == 547;
}
