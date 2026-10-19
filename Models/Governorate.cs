using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

/// <summary>
/// Jordanian governorate lookup. Fixed reference data seeded with the twelve
/// official governorates; it is independent of <see cref="Region"/>, which
/// remains the internal grouping used by grids, tariffs and technical criteria.
/// </summary>
public partial class Governorate
{
    public int Id { get; set; }

    /// <summary>Stable short code (e.g. "AM"), unique across the lookup.</summary>
    public string Code { get; set; } = null!;

    public string NameEn { get; set; } = null!;

    public string NameAr { get; set; } = null!;

    public virtual ICollection<Land> Lands { get; set; } = new List<Land>();
}
