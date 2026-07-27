using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IRegionService
{
    Task<List<RegionDto>> GetAllAsync();
    Task<RegionDto?> GetByIdAsync(int id);
    Task<RegionDto> CreateAsync(CreateRegionRequest request);
    Task<bool> UpdateAsync(int id, UpdateRegionRequest request);
    Task<bool> DeleteAsync(int id);
}