using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class DocumentType
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<LandDocument> LandDocuments { get; set; } = new List<LandDocument>();
}
