using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class LandCriterionService : ILandCriterionService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<LandCriterionService> _logger;

    public LandCriterionService(NebrasdbContext context, ILogger<LandCriterionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<LandCriterionDto>> GetAllAsync()
    {
        var items = await _context.LandCriteria.ToListAsync();
        var result = new List<LandCriterionDto>();
        foreach (var item in items) result.Add(item.Adapt<LandCriterionDto>());
        return result;
    }

    public async Task<LandCriterionDto?> GetByIdAsync(int id)
    {
        var item = await _context.LandCriteria.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<LandCriterionDto>();
    }

    public async Task<LandCriterionDto> CreateAsync(CreateLandCriterionRequest request)
    {
        var item = request.Adapt<LandCriterion>();
        _context.LandCriteria.Add(item);
        await _context.SaveChangesAsync();
        return item.Adapt<LandCriterionDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateLandCriterionRequest request)
    {
        var item = await _context.LandCriteria.FindAsync(id);
        if (item == null) return false;
        item.MinAreaDonum = request.MinAreaDonum;
        item.MaxSlopePct = request.MaxSlopePct;
        item.MaxGridDistanceKm = request.MaxGridDistanceKm;
        item.MinSolarIrradiance = request.MinSolarIrradiance;
        item.MinElevationM = request.MinElevationM;
        item.UpdatedById = request.UpdatedById;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _context.LandCriteria.FindAsync(id);
        if (item == null) return false;
        _context.LandCriteria.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }
}
