using Mapster;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class DocumentTypeService : IDocumentTypeService
{
    private readonly IRepository<DocumentType> _repository;
    private readonly ILogger<DocumentTypeService> _logger;

    public DocumentTypeService(IRepository<DocumentType> repository, ILogger<DocumentTypeService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<List<DocumentTypeDto>> GetAllAsync()
    {
        var items = await _repository.GetAllAsync();
        var result = new List<DocumentTypeDto>();
        foreach (var item in items) result.Add(item.Adapt<DocumentTypeDto>());
        return result;
    }

    public async Task<DocumentTypeDto?> GetByIdAsync(int id)
    {
        var item = await _repository.GetByIdAsync(id);
        if (item == null) return null;
        return item.Adapt<DocumentTypeDto>();
    }

    public async Task<DocumentTypeDto> CreateAsync(CreateDocumentTypeRequest request)
    {
        var item = request.Adapt<DocumentType>();
        await _repository.AddAsync(item);
        await _repository.SaveChangesAsync();
        return item.Adapt<DocumentTypeDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateDocumentTypeRequest request)
    {
        var item = await _repository.GetByIdAsync(id);
        if (item == null) return false;
        item.Name = request.Name;
        await _repository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var result = await _repository.DeleteAsync(id);
        if (result == false) return false;
        await _repository.SaveChangesAsync();
        return true;
    }
}
