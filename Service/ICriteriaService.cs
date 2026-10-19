using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface ICriteriaService
{
    Task<LandCriterionDto> GetCurrentAsync();

    Task<List<LandCriterionDto>> GetHistoryAsync();

    Task<LandCriterionDto> CreateAsync(CreateLandCriterionRequest request);
}
