using NibrasWeb.Enums;
using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class Offer
{
    public int Id { get; set; }

    public int LandId { get; set; }
    public Land Land { get; set; }

    public int InvestorId { get; set; }
    public User Investor { get; set; }
    public OfferStatus Status { get; set; }
    public ConnectionMechanism ConnectionMechanism { get; set; }
    public decimal CurrentAmount { get; set; }
    public int LastProposedById { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public decimal RequiredCapacityMw { get; set; }

    public int? AcceptedVersionId { get; set; }

    public int? RelatedToRejectedOfferId { get; set; }
    public virtual Offer? RelatedToRejectedOffer { get; set; }

    public virtual OfferVersion? AcceptedVersion { get; set; }

    public virtual Contract? Contract { get; set; }

    public virtual GridCapacityReservation? GridCapacityReservation { get; set; }

    public virtual ICollection<OfferVersion> OfferVersions { get; set; } = new List<OfferVersion>();

    public virtual ICollection<OfferNegotiationHistory> OfferNegotiationHistories { get; set; } = new List<OfferNegotiationHistory>();

}
