using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class GridCapacityReservationService : IGridCapacityReservationService
{
    private readonly IRepository<GridCapacityReservation> _repository;
    private readonly ILogger<GridCapacityReservationService> _logger;

    public GridCapacityReservationService(IRepository<GridCapacityReservation> repository, ILogger<GridCapacityReservationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<GridCapacityReservationDto>> GetAllAsync()
    {
        var items = await _repository.GetAllAsync();
        var result = new List<GridCapacityReservationDto>();
        foreach (var item in items) result.Add(item.Adapt<GridCapacityReservationDto>());
        return result;
    }

    public async Task<GridCapacityReservationDto?> GetByIdAsync(int id)
    {
        var item = await _repository.GetByIdAsync(id);
        if (item == null) return null;
        return item.Adapt<GridCapacityReservationDto>();
    }

    public async Task<GridCapacityReservationDto> CreateAsync(CreateGridCapacityReservationRequest request)
    {
        var item = request.Adapt<GridCapacityReservation>();
        await _repository.AddAsync(item);
        await _repository.SaveChangesAsync();
        return item.Adapt<GridCapacityReservationDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateGridCapacityReservationRequest request)
    {
        var item = await _repository.GetByIdAsync(id);
        if (item == null) return false;
        item.GridId = request.GridId;
        item.ContractId = request.ContractId;
        item.ReservedMw = request.ReservedMw;
        await _repository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var result = await _repository.DeleteAsync(id);
        if (result == false) return false;
        await _repository.SaveChangesAsync();
        return true;
    }
}
