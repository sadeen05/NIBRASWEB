using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

public interface IDocumentTypeService
{
    Task<List<DocumentTypeDto>> GetAllAsync();

    Task<DocumentTypeDto> CreateAsync(DocumentTypeRequest request);

    Task<DocumentTypeDto> UpdateAsync(int id, DocumentTypeRequest request);

    Task DeleteAsync(int id);
}
