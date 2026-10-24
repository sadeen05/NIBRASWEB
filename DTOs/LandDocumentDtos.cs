using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace NIBRAS.API.DTOs;

/// <summary>
/// The real upload body. The file is validated by signature and size in the storage service;
/// the browser-supplied content type is never trusted.
/// </summary>
public class UploadLandDocumentFileRequest
{
    [Required, Range(1, int.MaxValue)]
    public int DocumentTypeId { get; set; }

    [Required]
    public IFormFile? File { get; set; }
}

/// <summary>
/// Legacy metadata-only body. It is accepted only in Development for the historical regression
/// suites; a deployed environment must use the multipart file upload.
/// </summary>
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
    // The legacy metadata-only path is returned for historical rows. A real upload's private
    // storage key is never returned; clients use HasFile and the authenticated download action.
    string? FilePath,
    int Version,
    string Status,
    DateTime? UploadedAt,
    int? ReviewedById,
    DateTime? ReviewedAt,
    string? ReviewNote,
    string? OriginalFileName = null,
    string? ContentType = null,
    long? FileSizeBytes = null,
    string? Sha256 = null,
    bool HasFile = false);

/// <summary>A stored document stream with the safe headers the download endpoint needs.</summary>
public sealed record LandDocumentDownload(
    Stream Content,
    string FileName,
    string ContentType,
    long Length);
