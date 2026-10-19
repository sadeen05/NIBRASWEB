using Mapster;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class RegionService : IRegionService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<RegionService> _logger;

    public RegionService(NebrasdbContext context, ILogger<RegionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<RegionDto>> GetAllAsync()
    {
        var regions = await _context.Regions.ToListAsync();
        var result = new List<RegionDto>();
        foreach (var region in regions)
            result.Add(region.Adapt<RegionDto>());
        return result;
    }

    public async Task<RegionDto?> GetByIdAsync(int id)
    {
        var region = await _context.Regions.FindAsync(id);
        if (region == null) return null;
        return region.Adapt<RegionDto>();
    }

    public async Task<RegionDto> CreateAsync(CreateRegionRequest request)
    {
        var region = request.Adapt<Region>();
        _context.Regions.Add(region);
        await _context.SaveChangesAsync();
        return region.Adapt<RegionDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateRegionRequest request)
    {
        var region = await _context.Regions.FindAsync(id);
        if (region == null) return false;

        region.NameAr = request.NameAr;
        region.NameEn = request.NameEn;
        region.PeakSunHoursPerDay = request.PeakSunHoursPerDay;
        region.WheelingFeePerKwh = request.WheelingFeePerKwh;
        region.LossPercentage = request.LossPercentage;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var region = await _context.Regions.FindAsync(id);
        if (region == null) return false;
        _context.Regions.Remove(region);

        // A region is referenced by GRIDS and by LANDS. Deleting one that is still in use must be
        // refused with the same controlled conflict the rest of the API uses instead of surfacing
        // as an unexpected 500.
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            throw new BusinessRuleException("Cannot delete a region that is in use by grids or lands.");
        }

        return true;
    }

    // SQL Server reports a foreign-key violation as error 547; GRIDS and LANDS are the only tables
    // that reference REGIONS.
    private static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number == 547;
}
