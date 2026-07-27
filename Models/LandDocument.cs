using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class LandDocument
{
    public int Id { get; set; }

    public int LandId { get; set; }

    public int DocumentTypeId { get; set; }

    public string FilePath { get; set; } = null!;

    public int Version { get; set; }

    public string? Status { get; set; }

    public DateTime? UploadedAt { get; set; }

    public virtual DocumentType DocumentType { get; set; } = null!;

    public virtual Land Land { get; set; } = null!;
}
