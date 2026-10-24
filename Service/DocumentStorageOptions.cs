namespace NIBRAS.API.Services;

/// <summary>
/// Configuration of the private land-document store. The root is deliberately not under
/// <c>wwwroot</c>: documents are only served through the authenticated download endpoint, never
/// as static files.
/// </summary>
public sealed class DocumentStorageOptions
{
    public const string SectionName = "Documents";

    /// <summary>The hard upper bound for one stored file and for the default configuration.</summary>
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;

    /// <summary>Room for the multipart boundary, the form fields and headers.</summary>
    public const long UploadEnvelopeAllowanceBytes = 1 * 1024 * 1024;

    /// <summary>The one-action request cap; the configured file limit must fit inside it.</summary>
    public const long MaxUploadRequestBytes = DefaultMaxFileSizeBytes + UploadEnvelopeAllowanceBytes;

    /// <summary>
    /// Absolute path, or a path relative to the content root, of the private document store.
    /// An empty value uses <c>App_Data/land-documents</c> under the content root.
    /// </summary>
    public string StorageRoot { get; set; } = "";

    /// <summary>The maximum accepted file size. Defaults to 10 MiB and may not exceed it.</summary>
    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;
}
