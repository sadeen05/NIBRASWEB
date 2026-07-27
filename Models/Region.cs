using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class Region
{
    public int Id { get; set; }

    public string NameAr { get; set; } = null!;

    public string NameEn { get; set; } = null!;

    public virtual ICollection<Grid> Grids { get; set; } = new List<Grid>();

    public virtual ICollection<Land> Lands { get; set; } = new List<Land>();
}
