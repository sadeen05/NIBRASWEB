using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface ILandService
{
    Task<LandListResponse> GetAllAsync(int pageNumber, int pageSize, string? status);

    Task<LandDto> GetByIdAsync(int id);

    Task<LandDto> CreateAsync(CreateLandRequest request);

    Task UpdateAsync(int id, UpdateLandRequest request);

    Task SubmitAsync(int id);

    Task ReviseAsync(int id);

    Task VerifyAsync(int id);

    Task RejectAsync(int id, string reason);

    Task<LandEligibilityResponse> GetEligibilityAsync(int id);

    Task<List<LandStatusHistoryDto>> GetAllStatusHistoryAsync(int landId);
}
