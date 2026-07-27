using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class LandStatusHistoryService : ILandStatusHistoryService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<LandStatusHistoryService> _logger;

    public LandStatusHistoryService(NebrasdbContext context, ILogger<LandStatusHistoryService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<LandStatusHistoryDto>> GetAllAsync()
    {
        var items = await _context.LandStatusHistories.ToListAsync();
        var result = new List<LandStatusHistoryDto>();
        foreach (var item in items) result.Add(item.Adapt<LandStatusHistoryDto>());
        return result;
    }

    public async Task<LandStatusHistoryDto?> GetByIdAsync(int id)
    {
        var item = await _context.LandStatusHistories.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<LandStatusHistoryDto>();
    }

    public async Task<LandStatusHistoryDto> CreateAsync(CreateLandStatusHistoryRequest request)
    {
        var item = request.Adapt<LandStatusHistory>();
        _context.LandStatusHistories.Add(item);
        await _context.SaveChangesAsync();
        return item.Adapt<LandStatusHistoryDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateLandStatusHistoryRequest request)
    {
        var item = await _context.LandStatusHistories.FindAsync(id);
        if (item == null) return false;
        item.StatusId = request.StatusId;
        item.Reason = request.Reason;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _context.LandStatusHistories.FindAsync(id);
        if (item == null) return false;
        _context.LandStatusHistories.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }
}
