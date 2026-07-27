using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class LandStatusHistoryService : ILandStatusHistoryService
{
    private readonly IRepository<LandStatusHistory> _repository;
    private readonly ILogger<LandStatusHistoryService> _logger;

    public LandStatusHistoryService(IRepository<LandStatusHistory> repository, ILogger<LandStatusHistoryService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<LandStatusHistoryDto>> GetAllAsync()
    {
        var items = await _repository.GetAllAsync();
        var result = new List<LandStatusHistoryDto>();
        foreach (var item in items) result.Add(item.Adapt<LandStatusHistoryDto>());
        return result;
    }

    public async Task<LandStatusHistoryDto?> GetByIdAsync(int id)
    {
        var item = await _repository.GetByIdAsync(id);
        if (item == null) return null;
        return item.Adapt<LandStatusHistoryDto>();
    }

    public async Task<LandStatusHistoryDto> CreateAsync(CreateLandStatusHistoryRequest request)
    {
        var item = request.Adapt<LandStatusHistory>();
        await _repository.AddAsync(item);
        await _repository.SaveChangesAsync();
        return item.Adapt<LandStatusHistoryDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateLandStatusHistoryRequest request)
    {
        var item = await _repository.GetByIdAsync(id);
        if (item == null) return false;
        item.StatusId = request.StatusId;
        item.Reason = request.Reason;
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
