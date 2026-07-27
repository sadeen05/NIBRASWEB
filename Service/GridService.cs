using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class GridService : IGridService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<GridService> _logger;

    public GridService(NebrasdbContext context, ILogger<GridService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<GridDto>> GetAllAsync()
    {
        var grids = await _context.Grids.ToListAsync();
        var result = new List<GridDto>();
        foreach (var grid in grids) result.Add(grid.Adapt<GridDto>());
        return result;
    }

    public async Task<GridDto?> GetByIdAsync(int id)
    {
        var grid = await _context.Grids.FindAsync(id);
        if (grid == null) return null;
        return grid.Adapt<GridDto>();
    }

    public async Task<GridDto> CreateAsync(CreateGridRequest request)
    {
        var grid = request.Adapt<Grid>();
        _context.Grids.Add(grid);
        await _context.SaveChangesAsync();
        return grid.Adapt<GridDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateGridRequest request)
    {
        var grid = await _context.Grids
            .Include(g => g.GridCapacityReservations)
            .FirstOrDefaultAsync(g => g.Id == id);
        if (grid == null) return false;

        var totalReserved = grid.GridCapacityReservations.Sum(r => r.ReservedMw);
        if (request.CapacityMw < totalReserved)
            throw new InvalidOperationException(
                $"Cannot reduce capacity below {totalReserved} MW (already reserved).");

        grid.RegionId = request.RegionId;
        grid.Name = request.Name;
        grid.CapacityMw = request.CapacityMw;
        grid.Status = request.Status;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var hasReservations = await _context.GridCapacityReservations.AnyAsync(r => r.GridId == id);
        if (hasReservations)
            throw new InvalidOperationException("Cannot delete a grid that has capacity reservations.");

        var grid = await _context.Grids.FindAsync(id);
        if (grid == null) return false;

        _context.Grids.Remove(grid);
        await _context.SaveChangesAsync();
        return true;
    }
}
