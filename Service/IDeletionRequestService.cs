using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IDeletionRequestService
{
    Task<List<DeletionRequestDto>> GetAllAsync();
    Task<DeletionRequestDto?> GetByIdAsync(int id);
    Task<DeletionRequestDto> CreateAsync(CreateDeletionRequestRequest request);
    Task<bool> UpdateAsync(int id, UpdateDeletionRequestRequest request);
    Task<bool> DeleteAsync(int id);
}
