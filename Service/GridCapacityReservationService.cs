using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class GridCapacityReservationService : IGridCapacityReservationService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<GridCapacityReservationService> _logger;

    public GridCapacityReservationService(NebrasdbContext context, ILogger<GridCapacityReservationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<GridCapacityReservationDto>> GetAllAsync()
    {
        var items = await _context.GridCapacityReservations.ToListAsync();
        var result = new List<GridCapacityReservationDto>();
        foreach (var item in items) result.Add(item.Adapt<GridCapacityReservationDto>());
        return result;
    }

    public async Task<GridCapacityReservationDto?> GetByIdAsync(int id)
    {
        var item = await _context.GridCapacityReservations.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<GridCapacityReservationDto>();
    }

    public async Task<GridCapacityReservationDto> CreateAsync(CreateGridCapacityReservationRequest request)
    {
        var item = request.Adapt<GridCapacityReservation>();
        _context.GridCapacityReservations.Add(item);
        await _context.SaveChangesAsync();
        return item.Adapt<GridCapacityReservationDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateGridCapacityReservationRequest request)
    {
        var item = await _context.GridCapacityReservations.FindAsync(id);
        if (item == null) return false;
        item.GridId = request.GridId;
        item.ContractId = request.ContractId;
        item.ReservedMw = request.ReservedMw;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _context.GridCapacityReservations.FindAsync(id);
        if (item == null) return false;
        _context.GridCapacityReservations.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }
}
