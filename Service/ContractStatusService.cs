using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class ContractStatusService : IContractStatusService
{
    private readonly IRepository<ContractStatus> _repository;
    private readonly ILogger<ContractStatusService> _logger;

    public ContractStatusService(IRepository<ContractStatus> repository, ILogger<ContractStatusService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<ContractStatusDto>> GetAllAsync()
    {
        var items = await _repository.GetAllAsync();
        var result = new List<ContractStatusDto>();
        foreach (var item in items) result.Add(item.Adapt<ContractStatusDto>());
        return result;
    }

    public async Task<ContractStatusDto?> GetByIdAsync(int id)
    {
        var item = await _repository.GetByIdAsync(id);
        if (item == null) return null;
        return item.Adapt<ContractStatusDto>();
    }

    public async Task<ContractStatusDto> CreateAsync(CreateContractStatusRequest request)
    {
        var item = request.Adapt<ContractStatus>();
        await _repository.AddAsync(item);
        await _repository.SaveChangesAsync();
        return item.Adapt<ContractStatusDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateContractStatusRequest request)
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
