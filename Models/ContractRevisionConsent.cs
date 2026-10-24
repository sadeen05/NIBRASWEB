using System;

namespace NIBRAS.Models;

/// <summary>
/// One explicit party decision on a contract revision. A revision only advances after both
/// contract parties have a consent row with decision <c>Approved</c>. The unique index on
/// (revision, party) makes a later silent overwrite impossible.
/// </summary>
public partial class ContractRevisionConsent
{
    public int Id { get; set; }

    public int RevisionId { get; set; }

    public int PartyUserId { get; set; }

    public string PartyNameSnapshot { get; set; } = null!;

    public string PartyRoleSnapshot { get; set; } = null!;

    /// <summary>Approved or Rejected.</summary>
    public string Decision { get; set; } = null!;

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ContractRevision Revision { get; set; } = null!;
}
