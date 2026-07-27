using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface ILandStatusService
{
    Task<List<LandStatusDto>> GetAllAsync();
    Task<LandStatusDto?> GetByIdAsync(int id);
    Task<LandStatusDto> CreateAsync(CreateLandStatusRequest request);
    Task<bool> UpdateAsync(int id, UpdateLandStatusRequest request);
    Task<bool> DeleteAsync(int id);
}
