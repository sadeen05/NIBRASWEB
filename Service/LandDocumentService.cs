using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS.API.DTOs;
using NIBRAS.Models;

namespace NIBRAS.API.Services;

public class LandDocumentService : ILandDocumentService
{
    private readonly NebrasdbContext _context;
    private readonly ILogger<LandDocumentService> _logger;

    public LandDocumentService(NebrasdbContext context, ILogger<LandDocumentService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<LandDocumentDto>> GetAllAsync()
    {
        var items = await _context.LandDocuments.ToListAsync();
        var result = new List<LandDocumentDto>();
        foreach (var item in items) result.Add(item.Adapt<LandDocumentDto>());
        return result;
    }

    public async Task<LandDocumentDto?> GetByIdAsync(int id)
    {
        var item = await _context.LandDocuments.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<LandDocumentDto>();
    }

    public async Task<LandDocumentDto> CreateAsync(CreateLandDocumentRequest request)
    {
        var latestVersion = await _context.LandDocuments
            .Where(d => d.LandId == request.LandId && d.DocumentTypeId == request.DocumentTypeId)
            .OrderByDescending(d => d.Version)
            .Select(d => (int?)d.Version)
            .FirstOrDefaultAsync();

        var doc = new LandDocument
        {
            LandId = request.LandId,
            DocumentTypeId = request.DocumentTypeId,
            FilePath = request.FilePath,
            Version = (latestVersion ?? 0) + 1,
            Status = "Pending",
            UploadedAt = DateTime.UtcNow
        };

        _context.LandDocuments.Add(doc);
        await _context.SaveChangesAsync();
        return doc.Adapt<LandDocumentDto>();
    }

    public async Task<bool> UpdateAsync(int id, UpdateLandDocumentRequest request)
    {
        var doc = await _context.LandDocuments.FindAsync(id);
        if (doc == null) return false;

        // فقط Pending → Approved أو Pending → Rejected
        if (doc.Status != "Pending")
            throw new InvalidOperationException("Document is no longer pending. Cannot change status.");

        if (request.Status != "Approved" && request.Status != "Rejected")
            throw new InvalidOperationException("Status must be 'Approved' or 'Rejected'.");

        doc.DocumentTypeId = request.DocumentTypeId;
        doc.FilePath = request.FilePath;
        doc.Status = request.Status;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var doc = await _context.LandDocuments.FindAsync(id);
        if (doc == null) return false;

        _context.LandDocuments.Remove(doc);
        await _context.SaveChangesAsync();
        return true;
    }
}
