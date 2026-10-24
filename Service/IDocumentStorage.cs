using Microsoft.AspNetCore.Http;

namespace NIBRAS.API.Services;

/// <summary>The private storage metadata of one uploaded land document.</summary>
public sealed record StoredDocument(
    string RelativePath,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256);

/// <summary>
/// The private binary store for land documents. It accepts only PDF, JPEG and PNG files, checks
/// the magic bytes instead of trusting the browser-supplied content type, and never exposes the
/// storage path to a client. A file is written before the database row is committed; the caller
/// deletes it again when the transaction does not commit, so a failed upload cannot leave a row
/// without a file.
/// </summary>
public interface IDocumentStorage
{
    /// <summary>Validates, hashes and stores one upload; returns its private relative path.</summary>
    Task<StoredDocument> SaveAsync(
        IFormFile file,
        int landId,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a stored file for the authenticated download endpoint.</summary>
    Stream OpenRead(string relativePath);

    /// <summary>Best-effort removal of a stored file after a failed database write.</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}
