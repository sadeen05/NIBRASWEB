using System;

namespace NIBRAS.Models;

/// <summary>
/// One immutable contract-version record. A <c>Draft</c> row preserves the complete terms of
/// an actual contract draft; an <c>Event</c> row preserves a review, signature, rejection,
/// termination or cancellation event and points at the exact draft version it acted on.
/// Rows are append-only: the service never updates or deletes a version.
/// </summary>
public partial class ContractVersion
{
    public int Id { get; set; }

    public int ContractId { get; set; }

    /// <summary>Draft version number. Event rows carry the draft version they acted on.</summary>
    public int VersionNumber { get; set; }

    /// <summary><c>Draft</c> or <c>Event</c>.</summary>
    public string VersionKind { get; set; } = null!;

    /// <summary>Created, Signed, Reviewed, Rejected, Terminated or CancellationRequested; null for drafts.</summary>
    public string? EventType { get; set; }

    /// <summary>The contract status at the moment this row was recorded.</summary>
    public string Status { get; set; } = null!;

    /// <summary>The previous draft version for a later draft; null for version 1 and for events.</summary>
    public int? PreviousVersionId { get; set; }

    /// <summary>The exact draft version a signing/review/rejection/termination event acted on.</summary>
    public int? TargetVersionId { get; set; }

    public int OfferId { get; set; }

    public int? OfferVersionId { get; set; }

    /// <summary>Complete immutable terms snapshot as JSON (offer version terms plus contract facts).</summary>
    public string TermsSnapshot { get; set; } = null!;

    /// <summary>Revision, rejection, termination or cancellation reason when supplied.</summary>
    public string? Reason { get; set; }

    public int? CreatedById { get; set; }

    public string? CreatedByNameSnapshot { get; set; }

    public string? CreatedByRoleSnapshot { get; set; }

    public DateTime CreatedAt { get; set; }
}
