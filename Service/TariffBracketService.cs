using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class TariffBracketService : ITariffBracketService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<TariffBracketService> _logger;

    public TariffBracketService(NebrasdbContext context, ILogger<TariffBracketService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<TariffBracketDto>> GetAllAsync()
    {
        var brackets = await _context.TariffBrackets.ToListAsync();
        var result = new List<TariffBracketDto>();
        foreach (var b in brackets) result.Add(b.Adapt<TariffBracketDto>());
        return result;
    }

    public async Task<TariffBracketDto?> GetByIdAsync(int id)
    {
        var bracket = await _context.TariffBrackets.FindAsync(id);
        if (bracket == null) return null;
        return bracket.Adapt<TariffBracketDto>();
    }

    public async Task<TariffBracketDto> CreateAsync(CreateTariffBracketRequest request)
    {
        var region = await _context.Regions.FindAsync(request.RegionId);
        if (region == null)
            throw new KeyNotFoundException("Region not found.");

        if (request.FromKwh < 0)
            throw new InvalidOperationException("FromKwh must be non-negative.");

        if (request.ToKwh.HasValue && request.ToKwh <= request.FromKwh)
            throw new InvalidOperationException("ToKwh must be greater than FromKwh.");

        if (request.RatePerKwh < 0)
            throw new InvalidOperationException("RatePerKwh must be non-negative.");

        var bracket = request.Adapt<TariffBracket>();
        _context.TariffBrackets.Add(bracket);
        await _context.SaveChangesAsync();
        return bracket.Adapt<TariffBracketDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateTariffBracketRequest request)
    {
        var bracket = await _context.TariffBrackets.FindAsync(id);
        if (bracket == null) return false;

        var region = await _context.Regions.FindAsync(request.RegionId);
        if (region == null)
            throw new KeyNotFoundException("Region not found.");

        if (request.FromKwh < 0)
            throw new InvalidOperationException("FromKwh must be non-negative.");

        if (request.ToKwh.HasValue && request.ToKwh <= request.FromKwh)
            throw new InvalidOperationException("ToKwh must be greater than FromKwh.");

        if (request.RatePerKwh < 0)
            throw new InvalidOperationException("RatePerKwh must be non-negative.");

        bracket.RegionId = request.RegionId;
        bracket.FromKwh = request.FromKwh;
        bracket.ToKwh = request.ToKwh;
        bracket.RatePerKwh = request.RatePerKwh;
        bracket.EffectiveFrom = request.EffectiveFrom;
        bracket.EffectiveTo = request.EffectiveTo;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var bracket = await _context.TariffBrackets.FindAsync(id);
        if (bracket == null) return false;
        _context.TariffBrackets.Remove(bracket);
        await _context.SaveChangesAsync();
        return true;
    }
}
