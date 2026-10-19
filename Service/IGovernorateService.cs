using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IGovernorateService
{
    Task<List<GovernorateDto>> GetAllAsync();

    Task<GovernorateDto?> GetByIdAsync(int id);
}
