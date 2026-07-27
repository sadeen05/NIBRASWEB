using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class LandStatusHistory
{
    public int Id { get; set; }

    public int LandId { get; set; }

    public int StatusId { get; set; }

    public int ChangedById { get; set; }

    public string? Reason { get; set; }

    public DateTime? ChangedAt { get; set; }

    public virtual User ChangedBy { get; set; } = null!;

    public virtual Land Land { get; set; } = null!;
}
