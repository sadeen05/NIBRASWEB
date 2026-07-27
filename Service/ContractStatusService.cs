using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class ContractStatusService : IContractStatusService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<ContractStatusService> _logger;

    public ContractStatusService(NebrasdbContext context, ILogger<ContractStatusService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<ContractStatusDto>> GetAllAsync()
    {
        var items = await _context.ContractStatuses.ToListAsync();
        var result = new List<ContractStatusDto>();
        foreach (var item in items) result.Add(item.Adapt<ContractStatusDto>());
        return result;
    }

    public async Task<ContractStatusDto?> GetByIdAsync(int id)
    {
        var item = await _context.ContractStatuses.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<ContractStatusDto>();
    }

    public async Task<ContractStatusDto> CreateAsync(CreateContractStatusRequest request)
    {
        var item = request.Adapt<ContractStatus>();
        _context.ContractStatuses.Add(item);
        await _context.SaveChangesAsync();
        return item.Adapt<ContractStatusDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateContractStatusRequest request)
    {
        var item = await _context.ContractStatuses.FindAsync(id);
        if (item == null) return false;
        item.Name = request.Name;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _context.ContractStatuses.FindAsync(id);
        if (item == null) return false;
        _context.ContractStatuses.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }
}
