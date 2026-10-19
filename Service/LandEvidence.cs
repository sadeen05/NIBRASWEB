using Microsoft.EntityFrameworkCore;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

/// <summary>
/// The document-evidence rule shared by land visibility and the offer workflow: an approved
/// land is only "fully valid" against the criteria version it was verified against while the
/// latest version of every document type required for verification is <c>Approved</c>.
/// <para>
/// Only the latest version of a document type is the land's evidence, exactly as
/// <c>LandService.VerifyAsync</c> treats it. An older version that is Rejected or Pending after
/// a newer version exists is superseded history and never makes the land invalid on its own.
/// A required type with <em>no</em> uploaded version at all is outstanding as well: VerifyAsync
/// requires an approved latest version for every required type, so a type that has no rows (for
/// example a type that was made required after the land was verified) is missing evidence and
/// never a silent pass.
/// </para>
/// <para>
/// The state is derived from the rows on every read and never stored: a required document
/// rejected during a re-review, or an approval invalidated by an approved change request,
/// keeps the land out of the investor view until a corrected/latest version is Approved
/// again. The same rule is why making a document type optional may not cancel a specific
/// re-review rejection (<see cref="HasRejectedEvidenceForTypeAsync"/>). Nothing here mutates a
/// document, a version or a review.
/// </para>
/// <para>
/// The reads are deliberately flat (a required-type <c>IN</c> filter and a join, then the
/// latest-per-type reduction over the loaded rows, the same shape <c>VerifyAsync</c> uses) so
/// they cannot depend on a nested-subquery translation.
/// </para>
/// </summary>
public static class LandEvidence
{
    /// <summary>
    /// Ids of <c>Verified</c> lands that have at least one required document type whose latest
    /// version is not <c>Approved</c>, or that have no version of a required type at all. A land
    /// reaches this state when an approved change request invalidated its approvals, when a
    /// SuperAdmin rejected a required document during a re-review, or when a document type was
    /// made required after the land was verified.
    /// </summary>
    public static async Task<List<int>> VerifiedLandsWithOutstandingRequiredDocumentAsync(this NebrasdbContext context)
    {
        var requiredTypeIds = await context.RequiredDocumentTypeIdsAsync();
        if (requiredTypeIds.Count == 0)
            return [];

        // Candidate lands are read from the lands, not from the documents: a verified land that
        // has no row at all for a newly required type must be reported as outstanding, and it
        // would never appear in a document-driven query.
        var verifiedLandIds = await context.Lands
            .Where(l => l.Status == LandStatus.Verified)
            .Select(l => l.Id)
            .ToListAsync();

        var rows = await context.LandDocuments
            .Where(d => d.Land.Status == LandStatus.Verified && requiredTypeIds.Contains(d.DocumentTypeId))
            .Select(d => new { d.LandId, d.DocumentTypeId, d.Version, d.Status })
            .ToListAsync();

        // Per land, the required types whose latest version is Approved. Anything else (a latest
        // version that is Pending/Rejected, or a type with no row) is outstanding.
        var approvedTypesByLand = rows
            .GroupBy(d => d.LandId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(d => d.DocumentTypeId)
                    .Where(type => type.OrderByDescending(d => d.Version).First().Status == DocumentStatuses.Approved)
                    .Select(type => type.Key)
                    .ToHashSet());

        return verifiedLandIds
            .Where(landId => !requiredTypeIds.All(typeId =>
                approvedTypesByLand.TryGetValue(landId, out var approved) && approved.Contains(typeId)))
            .ToList();
    }

    /// <summary>
    /// Ids of the required document types of one land whose latest version is not
    /// <c>Approved</c>, or that have no uploaded version at all. These are the types the owner may
    /// correct (and the ones a SuperAdmin must review again on an approved land). Empty means the
    /// land's required evidence is complete.
    /// </summary>
    public static async Task<List<int>> OutstandingRequiredDocumentTypeIdsAsync(this NebrasdbContext context, int landId)
    {
        var requiredTypeIds = await context.RequiredDocumentTypeIdsAsync();
        if (requiredTypeIds.Count == 0)
            return [];

        var rows = await context.LandDocuments
            .Where(d => d.LandId == landId && requiredTypeIds.Contains(d.DocumentTypeId))
            .Select(d => new { d.DocumentTypeId, d.Version, d.Status })
            .ToListAsync();

        var approvedTypeIds = rows
            .GroupBy(d => d.DocumentTypeId)
            .Where(g => g.OrderByDescending(d => d.Version).First().Status == DocumentStatuses.Approved)
            .Select(g => g.Key)
            .ToHashSet();

        // Required types with no uploaded version are outstanding too (see the type summary).
        return requiredTypeIds.Where(typeId => !approvedTypeIds.Contains(typeId)).ToList();
    }

    /// <summary>
    /// True when the land has a required document type whose latest version is not
    /// <c>Approved</c> (or that has no version at all), i.e. its evidence is outstanding and it
    /// must not be presented to investors as fully valid, nor receive or be committed to offers.
    /// </summary>
    public static async Task<bool> HasOutstandingRequiredDocumentAsync(this NebrasdbContext context, int landId) =>
        (await context.OutstandingRequiredDocumentTypeIdsAsync(landId)).Count > 0;

    /// <summary>
    /// True when an already-approved land (<c>Verified</c> or <c>UnderContract</c>) has, as the
    /// latest version of the given document type, a document a reviewer rejected <em>after</em>
    /// that land was approved. On an approved land a rejection is only reachable through the
    /// SuperAdmin re-review (<c>LandService.ReviewDocumentAsync</c>, the latest version only, while
    /// the land's required evidence is outstanding), so this is exactly the specific rejection that
    /// keeps the land out of the investor view.
    /// <para>
    /// Making the type optional must not silently cancel it: <c>IS_REQUIRED_FOR_VERIFICATION</c> is
    /// documented to affect future verifications and the derived evidence, never to resolve a
    /// document a reviewer rejected on a land that is already approved, so
    /// <c>DocumentTypeService.UpdateAsync</c> refuses to clear the flag while this is true.
    /// </para>
    /// <para>
    /// A rejection recorded <em>before</em> the land's first approval is deliberately not reported:
    /// at approval that type was optional and <c>VerifyAsync</c> already accepted the rejection
    /// (it only requires the latest version of the types required at that moment), so it is not a
    /// decision the flag change could bypass. That exception is <em>fail-closed</em>: it applies
    /// only when the history proves it, i.e. a known review timestamp older than a known approval
    /// timestamp. A rejected version with no review timestamp, or an approved land with no recorded
    /// approval time, is indeterminate and is reported, so a missing or incomplete history can
    /// never become a silent bypass. The normal correction flow resolves it either way: a corrected
    /// upload becomes the latest version (no longer <c>Rejected</c>), after which the flag change is
    /// allowed again.
    /// </para>
    /// </summary>
    public static async Task<bool> HasRejectedEvidenceForTypeAsync(this NebrasdbContext context, int documentTypeId)
    {
        var rows = await context.LandDocuments
            .Where(d => d.DocumentTypeId == documentTypeId &&
                        (d.Land.Status == LandStatus.Verified || d.Land.Status == LandStatus.UnderContract))
            .Select(d => new { d.LandId, d.Version, d.Status, d.ReviewedAt })
            .ToListAsync();
        if (rows.Count == 0)
            return false;

        // The land's first approval. The read is flat and reduced in memory, like the ones above,
        // so it cannot depend on a nested-subquery translation.
        var landIds = rows.Select(r => r.LandId).Distinct().ToList();
        var history = await context.LandStatusHistories
            .Where(h => landIds.Contains(h.LandId) && h.Status == LandStatus.Verified && h.ChangedAt != null)
            .Select(h => new { h.LandId, h.ChangedAt })
            .ToListAsync();
        var approvedSince = history
            .GroupBy(h => h.LandId)
            .ToDictionary(g => g.Key, g => g.Min(h => h.ChangedAt!.Value));

        // Fail closed: only a rejection that is *provably* older than the land's first approval is
        // ignored. That needs both timestamps, so a rejected version with no review timestamp, or a
        // land whose approval time is not recorded, is reported as a re-review rejection instead of
        // being skipped. A missing or incomplete history can then never silently reveal the land.
        return rows
            .GroupBy(r => r.LandId)
            .Select(documents => documents.OrderByDescending(r => r.Version).First())
            .Any(latest =>
                latest.Status == DocumentStatuses.Rejected
                && (latest.ReviewedAt is not { } reviewedAt
                    || !approvedSince.TryGetValue(latest.LandId, out var since)
                    || reviewedAt >= since));
    }

    private static Task<List<int>> RequiredDocumentTypeIdsAsync(this NebrasdbContext context) =>
        context.DocumentTypes
            .Where(t => t.IsRequiredForVerification)
            .Select(t => t.Id)
            .ToListAsync();
}
