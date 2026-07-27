using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface ILandDocumentService
{
    Task<List<LandDocumentDto>> GetAllAsync();
    Task<LandDocumentDto?> GetByIdAsync(int id);
    Task<LandDocumentDto> CreateAsync(CreateLandDocumentRequest request);
    Task<bool> UpdateAsync(int id, UpdateLandDocumentRequest request);
    Task<bool> DeleteAsync(int id);
}
