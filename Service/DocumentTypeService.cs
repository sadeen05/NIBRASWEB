using Microsoft.Data.SqlClient;
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
        // The required→optional decision and the guard that refuses it run in one transaction
        // with the row pinned (UPDLOCK, HOLDLOCK - the same convention LockLandAsync uses).
        // A SuperAdmin re-review (LandService.ReviewDocumentAsync) rejects a document of this
        // type on an approved land under exactly this DOCUMENT_TYPES lock (taken after its
        // LANDS lock, so lock order never inverts), which makes the guard's read and this
        // write atomic against that rejection: either the guard sees it and refuses the
        // change, or the change commits first and the re-review is refused because the type
        // is no longer required. Without it, an update committing between the re-review's
        // evidence check and its write (or vice versa) drops the fresh rejection out of the
        // derived evidence and shows an approved land that a reviewer explicitly refused.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var documentType = await _context.DocumentTypes
            .FromSqlInterpolated($"SELECT * FROM DOCUMENT_TYPES WITH (UPDLOCK, HOLDLOCK) WHERE ID = {id}")
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException("Document type not found.");

        // Changing IsRequiredForVerification only affects future verifications. Lands
        // that are already Verified keep their recorded VerifiedAgainstCriterionId and
        // DataVerifiedByAdmin, and VerifyAsync only runs for PendingVerification lands,
        // so this update does not silently invalidate or rewrite an existing
        // verification snapshot.
        //
        // It must not silently cancel a specific rejection either. A document of this type that a
        // reviewer rejected after the land was approved (a SuperAdmin re-review rejection) is what
        // keeps that verified land hidden from investors, so clearing the flag is refused while one
        // exists: the flag is a rule about future verifications and the derived evidence, not a
        // resolution of a document a reviewer already refused. The land stays hidden and the type
        // stays required until the document is corrected and approved, which is the existing
        // re-review path (the owner may upload a correction because the type is still outstanding).
        // Making a type required and renaming it are unaffected, as is a rejection *provably*
        // recorded before the land was approved: that type was optional then, so VerifyAsync already
        // accepted it. The exception is fail-closed - a rejected version with no review timestamp,
        // or an approved land with no recorded approval time, is treated as a re-review rejection
        // (see LandEvidence.HasRejectedEvidenceForTypeAsync).
        if (documentType.IsRequiredForVerification &&
            !request.IsRequiredForVerification &&
            await _context.HasRejectedEvidenceForTypeAsync(id))
            throw new BusinessRuleException(
                "This document type cannot be made optional: an approved land has a document of this type that was rejected after the land was approved. It must be corrected and approved first.");

        documentType.Name = request.Name;
        documentType.IsRequiredForVerification = request.IsRequiredForVerification;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return ToDto(documentType);
    }

    public async Task DeleteAsync(int id)
    {
        var documentType = await _context.DocumentTypes.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException("Document type not found.");

        if (await _context.LandDocuments.AnyAsync(d => d.DocumentTypeId == id))
            throw new BusinessRuleException("This document type is used by uploaded documents and cannot be deleted.");

        _context.DocumentTypes.Remove(documentType);

        // The check above and the delete below are not atomic: an upload can insert a
        // LAND_DOCUMENTS row for this type in between, and the foreign key then rejects the
        // delete. That race enforces the same business rule as the check, so it must answer 409
        // like the check does instead of surfacing as an unexpected 500.
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            throw new BusinessRuleException(
                "This document type is used by uploaded documents and cannot be deleted.");
        }
    }

    // SQL Server reports a foreign-key violation as error 547. The constraint name is not matched
    // because LAND_DOCUMENTS is the only foreign key that references DOCUMENT_TYPES.
    private static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && sql.Number == 547;

    private static DocumentTypeDto ToDto(DocumentType documentType) =>
        new(documentType.Id, documentType.Name, documentType.IsRequiredForVerification);
}
