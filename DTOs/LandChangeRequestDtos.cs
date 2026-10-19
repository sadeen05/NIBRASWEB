using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

/// <summary>
/// The landlord-owned fields of a land. Used both as the immutable old/new snapshot
/// payload of a change request (serialized to JSON on the row) and as the structured
/// shape returned to the client, so the pre-change state is always recoverable.
/// Region, grid and the technical measurements are absent: they belong to the
/// SuperAdmin technical assessment and are never part of a landlord change request.
/// </summary>
public class LandFieldsSnapshot
{
    public int? GovernorateId { get; set; }
    public string LandNumber { get; set; } = "";
    public string Village { get; set; } = "";
    public string Basin { get; set; } = "";
    public string ParcelNumber { get; set; } = "";
    public decimal AreaDonum { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// A supporting document reference attached to a change request. Metadata only, the
/// same convention as <c>LAND_DOCUMENTS</c>: the backend stores a relative path.
/// </summary>
public class ChangeRequestDocumentDto
{
    [Required, Range(1, int.MaxValue)]
    public int DocumentTypeId { get; set; }

    [Required, NoControlCharacters, MaxLength(500), SafeRelativePath]
    public string FilePath { get; set; } = "";
}

/// <summary>
/// Landlord change request for an already-approved land. The proposed values are
/// validated with the same attributes as a normal edit; the region, grid and the
/// technical measurements cannot be proposed here.
/// </summary>
public class CreateLandChangeRequestRequest
{
    [Required, Range(1, int.MaxValue)]
    public int LandId { get; set; }

    [Required, NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string Reason { get; set; } = "";

    /// <summary>Optional so a legacy land needs no governorate; when supplied it is validated.</summary>
    [Range(1, int.MaxValue)]
    public int? GovernorateId { get; set; }

    [Required, NoControlCharacters, MaxLength(50)]
    public string LandNumber { get; set; } = "";

    [Required, NoControlCharacters, MaxLength(100)]
    public string Village { get; set; } = "";

    [Required, NoControlCharacters, MaxLength(100)]
    public string Basin { get; set; } = "";

    [Required, NoControlCharacters, MaxLength(100)]
    public string ParcelNumber { get; set; } = "";

    [Range(typeof(decimal), "0.0001", "99999999.99")]
    public decimal AreaDonum { get; set; }

    [NoControlCharactersExceptTabAndNewline, MaxLength(1000)]
    public string? Description { get; set; }

    public List<ChangeRequestDocumentDto> SupportingDocuments { get; set; } = new();
}

/// <summary>
/// SuperAdmin decision on a change request. <see cref="DecisionReason"/> is required
/// so every approval and rejection carries a recorded justification.
/// </summary>
public class ReviewLandChangeRequestRequest
{
    public bool Approved { get; set; }

    [Required, NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string DecisionReason { get; set; } = "";
}

/// <summary>
/// One change request. <c>OriginalFields</c> / <c>ProposedFields</c> are the immutable
/// snapshots stored on the row; the reviewer identity, timestamp and decision are kept
/// alongside them. A snapshot that cannot be read is reported as <c>null</c> and never
/// substituted with an empty or repaired one; <c>SupportingDocuments</c> follows the same
/// rule (an empty list means the request stored no references).
/// </summary>
public record LandChangeRequestDto(
    int Id,
    int LandId,
    string LandNumber,
    string Status,
    string Reason,
    DateTime RequestedAt,
    int RequestedById,
    string RequestedByName,
    LandFieldsSnapshot? OriginalFields,
    LandFieldsSnapshot? ProposedFields,
    IReadOnlyList<ChangeRequestDocumentDto>? SupportingDocuments,
    int? ReviewedById,
    string? ReviewedByName,
    DateTime? ReviewedAt,
    string? DecisionReason);
