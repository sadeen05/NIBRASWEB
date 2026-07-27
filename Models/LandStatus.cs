using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class LandStatus
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<Land> Lands { get; set; } = new List<Land>();
}
