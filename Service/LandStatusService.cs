using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class LandStatusService : ILandStatusService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<LandStatusService> _logger;

    public LandStatusService(NebrasdbContext context, ILogger<LandStatusService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<LandStatusDto>> GetAllAsync()
    {
        var items = await _context.LandStatuses.ToListAsync();
        var result = new List<LandStatusDto>();
        foreach (var item in items) result.Add(item.Adapt<LandStatusDto>());
        return result;
    }

    public async Task<LandStatusDto?> GetByIdAsync(int id)
    {
        var item = await _context.LandStatuses.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<LandStatusDto>();
    }

    public async Task<LandStatusDto> CreateAsync(CreateLandStatusRequest request)
    {
        var item = request.Adapt<LandStatus>();
        _context.LandStatuses.Add(item);
        await _context.SaveChangesAsync();
        return item.Adapt<LandStatusDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateLandStatusRequest request)
    {
        var item = await _context.LandStatuses.FindAsync(id);
        if (item == null) return false;
        item.Name = request.Name;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _context.LandStatuses.FindAsync(id);
        if (item == null) return false;
        _context.LandStatuses.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }
}
