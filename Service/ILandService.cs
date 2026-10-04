using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface ILandService
{
    Task<List<LandDto>> GetAllAsync(int pageNumber, int pageSize);

    Task<LandDto?> GetByIdAsync(int id);

    Task<LandDto> CreateAsync(CreateLandRequest request);

    Task<bool> UpdateAsync(int id, UpdateLandRequest request);

    Task<bool> DeleteAsync(int id);

    Task<bool> SubmitAsync(int landId, int landlordId);
    Task<bool> VerifyAsync(int landId, int adminId);

    Task<bool> RejectAsync(int landId, int adminId, string reason);

    Task<bool> CheckEligibilityAsync(int landId);

    Task<List<LandStatusHistoryDto>> GetAllStatusHistoryAsync(int landId);
}