using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class GridService : IGridService
{
    private readonly IRepository<Grid> _gridRepository;
    private readonly ILogger<GridService> _logger;

    public GridService(IRepository<Grid> gridRepository, ILogger<GridService> logger)
    {
        _gridRepository = gridRepository;
        _logger = logger;
    }

    public async Task<List<GridDto>> GetAllAsync()
    {
        var grids = await _gridRepository.GetAllAsync();
        var result = new List<GridDto>();

        foreach (var grid in grids)
        {
            result.Add(grid.Adapt<GridDto>());
        }

        return result;
    }

    public async Task<GridDto?> GetByIdAsync(int id)
    {
        var grid = await _gridRepository.GetByIdAsync(id);
        if (grid == null) return null;
        return grid.Adapt<GridDto>();
    }

    public async Task<GridDto> CreateAsync(CreateGridRequest request)
    {
        var grid = request.Adapt<Grid>();
        await _gridRepository.AddAsync(grid);
        await _gridRepository.SaveChangesAsync();
        return grid.Adapt<GridDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateGridRequest request)
    {
        var grid = await _gridRepository.GetByIdAsync(id);
        if (grid == null) return false;

        grid.RegionId = request.RegionId;
        grid.Name = request.Name;
        grid.CapacityMw = request.CapacityMw;
        grid.Status = request.Status;

        await _gridRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var result = await _gridRepository.DeleteAsync(id);
        if (result == false) return false;
        await _gridRepository.SaveChangesAsync();
        return true;
    }
}
