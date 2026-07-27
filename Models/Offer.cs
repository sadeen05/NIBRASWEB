using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class Offer
{
    public int Id { get; set; }

    public int LandId { get; set; }

    public int InvestorId { get; set; }

    public decimal RequiredCapacityMw { get; set; }

    public int StatusId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public virtual Contract? Contract { get; set; }

    public virtual User Investor { get; set; } = null!;

    public virtual Land Land { get; set; } = null!;

    public virtual ICollection<OfferVersion> OfferVersions { get; set; } = new List<OfferVersion>();

    public virtual OfferStatus Status { get; set; } = null!;
}
