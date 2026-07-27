using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class Region
{
    public int Id { get; set; }

    public string NameAr { get; set; } = null!;

    public string NameEn { get; set; } = null!;

    public decimal PeakSunHoursPerDay { get; set; }

    public decimal WheelingFeePerKwh { get; set; }

    public decimal LossPercentage { get; set; }

    public virtual ICollection<Grid> Grids { get; set; } = new List<Grid>();

    public virtual ICollection<Land> Lands { get; set; } = new List<Land>();

    public virtual ICollection<TariffBracket> TariffBrackets { get; set; } = new List<TariffBracket>();
}
