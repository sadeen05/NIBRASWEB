using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class LandStatusService : ILandStatusService
{
    private readonly IRepository<LandStatus> _repository;
    private readonly ILogger<LandStatusService> _logger;

    public LandStatusService(IRepository<LandStatus> repository, ILogger<LandStatusService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<LandStatusDto>> GetAllAsync()
    {
        var items = await _repository.GetAllAsync();
        var result = new List<LandStatusDto>();
        foreach (var item in items) result.Add(item.Adapt<LandStatusDto>());
        return result;
    }

    public async Task<LandStatusDto?> GetByIdAsync(int id)
    {
        var item = await _repository.GetByIdAsync(id);
        if (item == null) return null;
        return item.Adapt<LandStatusDto>();
    }

    public async Task<LandStatusDto> CreateAsync(CreateLandStatusRequest request)
    {
        var item = request.Adapt<LandStatus>();
        await _repository.AddAsync(item);
        await _repository.SaveChangesAsync();
        return item.Adapt<LandStatusDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateLandStatusRequest request)
    {
        var item = await _repository.GetByIdAsync(id);
        if (item == null) return false;
        item.Name = request.Name;
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
