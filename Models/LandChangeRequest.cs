using System;
using System.Collections.Generic;

namespace NIBRAS.Models;

/// <summary>
/// A landlord request to change the data of an already-approved (<c>Verified</c>)
/// land. The original and the proposed field values are preserved as immutable JSON
/// snapshots, together with the reason, the supporting documents, the requester and
/// reviewer identities, the timestamps and the final decision. A row is only ever
/// written once on submission and once on the review (to record the decision); the
/// snapshots are never rewritten, so the pre-change state can always be recovered.
/// </summary>
public partial class LandChangeRequest
{
    public int Id { get; set; }

    public int LandId { get; set; }

    /// <summary>Actor taken from the authenticated token, never from the request.</summary>
    public int RequestedById { get; set; }

    public string Reason { get; set; } = null!;

    /// <summary>One of <see cref="NibrasWeb.Enums.ChangeRequestStatuses"/>.</summary>
    public string Status { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    public int? ReviewedById { get; set; }

    public DateTime? ReviewedAt { get; set; }

    /// <summary>The reviewer's decision note (mandatory on a rejection).</summary>
    public string? DecisionReason { get; set; }

    /// <summary>Immutable JSON snapshot of the land's fields before the change.</summary>
    public string OldSnapshot { get; set; } = null!;

    /// <summary>Immutable JSON snapshot of the fields the landlord proposed.</summary>
    public string NewSnapshot { get; set; } = null!;

    /// <summary>JSON array of supporting document references (may be null/empty).</summary>
    public string? SupportingDocuments { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (SQL Server <c>rowversion</c>). The decision path
    /// loads the row and its UPDATE carries this value in the WHERE clause, so two
    /// concurrent decisions cannot both win: the loser gets a
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> and a 409
    /// instead of silently overwriting the winner's decision.
    /// </summary>
    public byte[] RowVersion { get; set; } = null!;

    public virtual Land Land { get; set; } = null!;

    public virtual User RequestedBy { get; set; } = null!;

    public virtual User? ReviewedBy { get; set; }
}
