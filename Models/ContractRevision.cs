using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

/// <summary>
/// One pre-signing contract-revision request. The row records the exact proposed terms,
/// who requested them, every party consent, the Admin decision (including escalation),
/// and the SuperAdmin decision. Terminal revisions are closed; the row is never deleted
/// or rewritten into a different decision. The immutable contract draft/event timeline
/// remains in <see cref="ContractVersion"/> and the decision audit remains in
/// <see cref="ContractReview"/>.
/// </summary>
public partial class ContractRevision
{
    public int Id { get; set; }

    public int ContractId { get; set; }

    /// <summary>The exact draft that was current when the revision was requested.</summary>
    public int BaseDraftVersionId { get; set; }

    /// <summary>PendingPartyConsent, PendingAdminReview, PendingSuperAdminReview, Approved, Rejected or Cancelled.</summary>
    public string Status { get; set; } = null!;

    /// <summary>Reason supplied by the requesting party.</summary>
    public string Reason { get; set; } = null!;

    /// <summary>Complete proposed terms (ContractVersionTermsDto JSON), including contract IDs and notice period.</summary>
    public string ProposedTermsSnapshot { get; set; } = null!;

    public int ProposedById { get; set; }

    public string ProposedByNameSnapshot { get; set; } = null!;

    public string ProposedByRoleSnapshot { get; set; } = null!;

    public DateTime ProposedAt { get; set; }

    public int? AdminReviewedById { get; set; }

    public string? AdminReviewedByNameSnapshot { get; set; }

    public string? AdminReviewedByRoleSnapshot { get; set; }

    public DateTime? AdminReviewedAt { get; set; }

    /// <summary>Approved or Escalated.</summary>
    public string? AdminDecision { get; set; }

    /// <summary>Mandatory when the Admin escalates the revision to a SuperAdmin.</summary>
    public string? EscalationReason { get; set; }

    public int? SuperAdminDecidedById { get; set; }

    public string? SuperAdminDecidedByNameSnapshot { get; set; }

    public string? SuperAdminDecidedByRoleSnapshot { get; set; }

    public DateTime? SuperAdminDecidedAt { get; set; }

    /// <summary>Continued or Cancelled.</summary>
    public string? SuperAdminDecision { get; set; }

    /// <summary>Reason for continuation or cancellation; mandatory for cancellation.</summary>
    public string? SuperAdminReason { get; set; }

    /// <summary>The new accepted offer version created by an authorised approval; null until then.</summary>
    public int? NewOfferVersionId { get; set; }

    /// <summary>The new immutable contract draft created by an authorised approval; null until then.</summary>
    public int? NewDraftVersionId { get; set; }

    /// <summary>Set exactly when the revision reaches a terminal state; null while open.</summary>
    public DateTime? ClosedAt { get; set; }

    public virtual ICollection<ContractRevisionConsent> Consents { get; set; } = new List<ContractRevisionConsent>();
}
