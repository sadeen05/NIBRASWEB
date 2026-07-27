using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class TariffBracket
{
    public int Id { get; set; }

    public int RegionId { get; set; }

    public int FromKwh { get; set; }

    public int? ToKwh { get; set; }

    public decimal RatePerKwh { get; set; }

    public DateTime EffectiveFrom { get; set; }

    public DateTime? EffectiveTo { get; set; }

    public virtual Region Region { get; set; } = null!;
}
