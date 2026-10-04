using System;
using NibrasWeb.Enums;

namespace NIBRAS.Models;

public partial class OfferNegotiationHistory
{
    public int Id { get; set; }

    public int OfferId { get; set; }

    public OfferHistoryActionType ActionType { get; set; }

    public int ActorId { get; set; }

    public decimal? Amount { get; set; }

    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User Actor { get; set; } = null!;

    public virtual Offer Offer { get; set; } = null!;
}
