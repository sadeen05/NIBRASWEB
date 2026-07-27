using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface ILandStatusHistoryService
{
    Task<List<LandStatusHistoryDto>> GetAllAsync();
    Task<LandStatusHistoryDto?> GetByIdAsync(int id);
    Task<LandStatusHistoryDto> CreateAsync(CreateLandStatusHistoryRequest request);
    Task<bool> UpdateAsync(int id, UpdateLandStatusHistoryRequest request);
    Task<bool> DeleteAsync(int id);
}
