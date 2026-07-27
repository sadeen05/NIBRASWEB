using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class Contract
{
    public int Id { get; set; }

    public int OfferId { get; set; }

    public int LandId { get; set; }

    public int InvestorId { get; set; }

    public int LandlordId { get; set; }

    public int StatusId { get; set; }

    public DateTime? InvestorSignedAt { get; set; }

    public DateTime? AdminReviewedAt { get; set; }

    public DateTime? LandlordSignedAt { get; set; }

    public DateTime? AdminSignedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<ContractReview> ContractReviews { get; set; } = new List<ContractReview>();

    public virtual GridCapacityReservation? GridCapacityReservation { get; set; }

    public virtual User Investor { get; set; } = null!;

    public virtual Land Land { get; set; } = null!;

    public virtual User Landlord { get; set; } = null!;

    public virtual Offer Offer { get; set; } = null!;

    public virtual ContractStatus Status { get; set; } = null!;
}
