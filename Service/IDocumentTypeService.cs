using NIBRAS.API.DTOs;
namespace NIBRAS.API.Services;

public interface IDocumentTypeService
{
    Task<List<DocumentTypeDto>> GetAllAsync();
    Task<DocumentTypeDto?> GetByIdAsync(int id);
    Task<DocumentTypeDto> CreateAsync(CreateDocumentTypeRequest request);
    Task<bool> UpdateAsync(int id, UpdateDocumentTypeRequest request);
    Task<bool> DeleteAsync(int id);
}
