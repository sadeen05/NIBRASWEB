using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class ContractReview
{
    public int Id { get; set; }

    public int ContractId { get; set; }

    public int ReviewerId { get; set; }

    public string Decision { get; set; } = null!;

    public string? Reason { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Contract Contract { get; set; } = null!;

    public virtual User Reviewer { get; set; } = null!;
}
