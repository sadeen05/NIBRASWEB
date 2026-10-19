using Mapster;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NIBRAS;
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
            throw new BusinessRuleException(
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
            throw new BusinessRuleException("Cannot delete a grid that has capacity reservations.");

        var grid = await _context.Grids.FindAsync(id);
        if (grid == null) return false;

        _context.Grids.Remove(grid);

        // The reservation check above is not the only reference a grid can have: LANDS.GRID_ID
        // also points at it, and a reservation can be created between that check and this delete.
        // The foreign key then rejects the delete - the same business rule as the check - so it
        // must answer 409 instead of surfacing as an unexpected 500.
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            throw new BusinessRuleException(
                "Cannot delete a grid that is in use by lands or capacity reservations.");
        }

        return true;
    }

    // SQL Server reports a foreign-key violation as error 547; LANDS and GRID_CAPACITY_RESERVATIONS
    // are the only tables that reference GRIDS.
    private static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number == 547;
}
