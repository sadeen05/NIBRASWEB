using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class OfferVersion
{
    public int Id { get; set; }

    public int OfferId { get; set; }

    public int VersionNumber { get; set; }

    public decimal? LandlordSharePct { get; set; }

    public int? DurationYears { get; set; }

    public DateOnly? StartDate { get; set; }

    public decimal? InstallationCost { get; set; }

    public int CreatedById { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual User CreatedBy { get; set; } = null!;

    public virtual Offer Offer { get; set; } = null!;
}
