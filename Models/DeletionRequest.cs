using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class DeletionRequest
{
    public int Id { get; set; }

    public int LandId { get; set; }

    public int RequestedById { get; set; }

    public string Reason { get; set; } = null!;

    public string? Status { get; set; }

    public string? AdminComment { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Land Land { get; set; } = null!;

    public virtual User RequestedBy { get; set; } = null!;
}
