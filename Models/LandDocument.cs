using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class LandDocument
{
    public int Id { get; set; }

    public int LandId { get; set; }

    public int DocumentTypeId { get; set; }

    /// <summary>
    /// The private relative storage key for a real upload, or the legacy client-supplied path
    /// for a metadata-only row. The value is never returned as a filesystem path to a client.
    /// </summary>
    public string FilePath { get; set; } = null!;

    /// <summary>The original display name of a real upload. NULL for a legacy metadata row.</summary>
    public string? OriginalFileName { get; set; }

    /// <summary>The detected media type of a real upload. NULL for a legacy metadata row.</summary>
    public string? ContentType { get; set; }

    /// <summary>The exact stored size in bytes. NULL for a legacy metadata row.</summary>
    public long? FileSizeBytes { get; set; }

    /// <summary>The lowercase SHA-256 of the stored bytes. NULL for a legacy metadata row.</summary>
    public string? Sha256 { get; set; }

    public int Version { get; set; }

    public string? Status { get; set; }

    public DateTime? UploadedAt { get; set; }

    public int? ReviewedById { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? ReviewNote { get; set; }

    public virtual DocumentType DocumentType { get; set; } = null!;

    public virtual Land Land { get; set; } = null!;

    public virtual User? ReviewedBy { get; set; }
}
