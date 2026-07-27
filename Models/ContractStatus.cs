using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

public partial class ContractStatus
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();
}
