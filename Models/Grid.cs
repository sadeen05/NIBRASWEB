using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class Grid
{
    public int Id { get; set; }

    public int RegionId { get; set; }

    public string Name { get; set; } = null!;

    public decimal CapacityMw { get; set; }

    public string? Status { get; set; }

    public virtual ICollection<GridCapacityReservation> GridCapacityReservations { get; set; } = new List<GridCapacityReservation>();

    public virtual Region Region { get; set; } = null!;
}
