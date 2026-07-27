using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class OfferStatus
{
    public int Id { get; set; }

    public string NameStatus { get; set; } = null!;

    public virtual ICollection<Offer> Offers { get; set; } = new List<Offer>();
}
