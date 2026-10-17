using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class DocumentTypeService : IDocumentTypeService
{
    private readonly NebrasdbContext _context;

    public DocumentTypeService(NebrasdbContext context)
    {
        _context = context;
    }

    public async Task<List<DocumentTypeDto>> GetAllAsync()
    {
        return await _context.DocumentTypes
            .AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new DocumentTypeDto(t.Id, t.Name, t.IsRequiredForVerification))
            .ToListAsync();
    }

    public async Task<DocumentTypeDto> CreateAsync(DocumentTypeRequest request)
    {
        var documentType = new DocumentType
        {
            Name = request.Name,
            IsRequiredForVerification = request.IsRequiredForVerification
        };

        _context.DocumentTypes.Add(documentType);
        await _context.SaveChangesAsync();

        return ToDto(documentType);
    }

    public async Task<DocumentTypeDto> UpdateAsync(int id, DocumentTypeRequest request)
    {
        var documentType = await _context.DocumentTypes.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException("Document type not found.");

        // Changing IsRequiredForVerification only affects future verifications. Lands
        // that are already Verified keep their recorded VerifiedAgainstCriterionId and
        // DataVerifiedByAdmin, and VerifyAsync only runs for PendingVerification lands,
        // so this update does not silently invalidate or rewrite an existing
        // verification snapshot.
        documentType.Name = request.Name;
        documentType.IsRequiredForVerification = request.IsRequiredForVerification;

        await _context.SaveChangesAsync();
        return ToDto(documentType);
    }

    public async Task DeleteAsync(int id)
    {
        var documentType = await _context.DocumentTypes.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException("Document type not found.");

        if (await _context.LandDocuments.AnyAsync(d => d.DocumentTypeId == id))
            throw new BusinessRuleException("This document type is used by uploaded documents and cannot be deleted.");

        _context.DocumentTypes.Remove(documentType);
        await _context.SaveChangesAsync();
    }

    private static DocumentTypeDto ToDto(DocumentType documentType) =>
        new(documentType.Id, documentType.Name, documentType.IsRequiredForVerification);
}
