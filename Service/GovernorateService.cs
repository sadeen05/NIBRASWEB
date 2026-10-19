using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

/// <summary>
/// Read-only access to the fixed governorate lookup. The twelve rows are seeded
/// reference data; there is no write surface in Phase 1.
/// </summary>
public class GovernorateService : IGovernorateService
{
    private readonly NebrasdbContext _context;

    public GovernorateService(NebrasdbContext context)
    {
        _context = context;
    }

    public async Task<List<GovernorateDto>> GetAllAsync()
    {
        var governorates = await _context.Governorates
            .AsNoTracking()
            .OrderBy(g => g.Id)
            .ToListAsync();

        var result = new List<GovernorateDto>();
        foreach (var governorate in governorates)
            result.Add(governorate.Adapt<GovernorateDto>());
        return result;
    }

    public async Task<GovernorateDto?> GetByIdAsync(int id)
    {
        var governorate = await _context.Governorates.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        if (governorate == null) return null;
        return governorate.Adapt<GovernorateDto>();
    }
}
