using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class GridCapacityReservation
{
    public int Id { get; set; }

    public int GridId { get; set; }

    public int ContractId { get; set; }

    public decimal ReservedMw { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Contract Contract { get; set; } = null!;

    public virtual Grid Grid { get; set; } = null!;
}
