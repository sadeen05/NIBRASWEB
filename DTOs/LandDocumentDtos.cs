using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public class UploadLandDocumentRequest
{
    [Required, Range(1, int.MaxValue)]
    public int DocumentTypeId { get; set; }

    [Required, NoControlCharacters, MaxLength(500), SafeRelativePath]
    public string FilePath { get; set; } = "";
}

public class ReviewLandDocumentRequest
{
    public bool Approved { get; set; }

    [NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string? Note { get; set; }
}

public class RejectLandRequest
{
    [Required, NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string Reason { get; set; } = "";
}

public record LandDocumentDto(
    int Id,
    int LandId,
    int DocumentTypeId,
    string DocumentTypeName,
    string FilePath,
    int Version,
    string Status,
    DateTime? UploadedAt,
    int? ReviewedById,
    DateTime? ReviewedAt,
    string? ReviewNote);
