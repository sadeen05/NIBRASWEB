using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface ILandCriterionService
{
    Task<List<LandCriterionDto>> GetAllAsync();
    Task<LandCriterionDto?> GetByIdAsync(int id);
    Task<LandCriterionDto> CreateAsync(CreateLandCriterionRequest request);
    Task<bool> UpdateAsync(int id, UpdateLandCriterionRequest request);
    Task<bool> DeleteAsync(int id);
}
