using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class RegionService : IRegionService
{
    private readonly IRepository<Region> _regionRepository;
    private readonly ILogger<RegionService> _logger;

    public RegionService(IRepository<Region> regionRepository, ILogger<RegionService> logger)
    {
        _regionRepository = regionRepository;
        _logger = logger;
    }

    public async Task<List<RegionDto>> GetAllAsync()
    {
        var regions = await _regionRepository.GetAllAsync();
        var result = new List<RegionDto>();

        foreach (var region in regions)
        {
            result.Add(region.Adapt<RegionDto>());
        }

        return result;
    }

    public async Task<RegionDto?> GetByIdAsync(int id)
    {
        var region = await _regionRepository.GetByIdAsync(id);
        if (region == null) return null;
        return region.Adapt<RegionDto>();
    }

    public async Task<RegionDto> CreateAsync(CreateRegionRequest request)
    {
        var region = request.Adapt<Region>();
        await _regionRepository.AddAsync(region);
        await _regionRepository.SaveChangesAsync();
        return region.Adapt<RegionDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateRegionRequest request)
    {
        var region = await _regionRepository.GetByIdAsync(id);
        if (region == null) return false;

        region.NameAr = request.NameAr;
        region.NameEn = request.NameEn;

        await _regionRepository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var result = await _regionRepository.DeleteAsync(id);
        if (result == false) return false;
        await _regionRepository.SaveChangesAsync();
        return true;
    }
}
