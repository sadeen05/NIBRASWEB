using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class DocumentTypeService : IDocumentTypeService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<DocumentTypeService> _logger;

    public DocumentTypeService(NebrasdbContext context, ILogger<DocumentTypeService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<DocumentTypeDto>> GetAllAsync()
    {
        var items = await _context.DocumentTypes.ToListAsync();
        var result = new List<DocumentTypeDto>();
        foreach (var item in items) result.Add(item.Adapt<DocumentTypeDto>());
        return result;
    }

    public async Task<DocumentTypeDto?> GetByIdAsync(int id)
    {
        var item = await _context.DocumentTypes.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<DocumentTypeDto>();
    }

    public async Task<DocumentTypeDto> CreateAsync(CreateDocumentTypeRequest request)
    {
        var item = request.Adapt<DocumentType>();
        _context.DocumentTypes.Add(item);
        await _context.SaveChangesAsync();
        return item.Adapt<DocumentTypeDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateDocumentTypeRequest request)
    {
        var item = await _context.DocumentTypes.FindAsync(id);
        if (item == null) return false;
        item.Name = request.Name;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _context.DocumentTypes.FindAsync(id);
        if (item == null) return false;
        _context.DocumentTypes.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }
}
