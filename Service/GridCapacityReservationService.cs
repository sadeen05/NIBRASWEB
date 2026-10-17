using Mapster;
using Microsoft.EntityFrameworkCore;
using NIBRAS;
using NIBRAS.API.DTOs;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

/// <summary>
/// Read-only reservation access plus the Admin release operation (BR-G2/BR-G3).
/// Reservations are created only by workflow code (offer approval / contract creation);
/// the public create/update/delete surface was removed on purpose.
/// </summary>
public class GridCapacityReservationService : IGridCapacityReservationService
{
    // GridCapacityReservation.ReleaseReason column length (nvarchar(300)).
    private const int ReleaseReasonMaxLength = 300;
    private const string DefaultReleaseReason = "Released by admin.";

    // Decision C: a reservation owned by a live contract is released by the contract
    // workflow (reject / terminate), never manually.
    private const string LiveContractReleaseMessage =
        "This reservation belongs to a live contract. Reject or terminate the contract through the normal workflow to release it.";

    private readonly NebrasdbContext _context;

    public GridCapacityReservationService(NebrasdbContext context)
    {
        _context = context;
    }

    public async Task<List<GridCapacityReservationDto>> GetAllAsync()
    {
        var items = await _context.GridCapacityReservations.ToListAsync();
        var result = new List<GridCapacityReservationDto>();
        foreach (var item in items) result.Add(item.Adapt<GridCapacityReservationDto>());
        return result;
    }

    public async Task<GridCapacityReservationDto?> GetByIdAsync(int id)
    {
        var item = await _context.GridCapacityReservations.FindAsync(id);
        if (item == null) return null;
        return item.Adapt<GridCapacityReservationDto>();
    }

    /// <summary>
    /// Releases a reservation by stamping <c>ReleasedAt</c> — the row is never deleted
    /// (BR-G2/BR-G3), so capacity accounting stays intact.
    /// Returns <c>null</c> when the reservation does not exist (caller maps this to 404).
    /// A reservation that is already released is returned unchanged (idempotent):
    /// <c>ReleasedAt</c> and <c>ReleaseReason</c> are never overwritten, so a repeated or
    /// concurrent release cannot release it twice or alter the original audit trail.
    /// A reservation owned by a LIVE contract (<c>PendingSignatures</c> or <c>Active</c>)
    /// cannot be released here — the contract workflow releases it on reject/terminate.
    /// <para>
    /// Concurrency: the reservation row is pinned with <c>UPDLOCK, HOLDLOCK</c> inside a
    /// transaction, so a concurrent release serialises on the row (the loser re-reads the
    /// released row and returns it unchanged) and a concurrent contract creation — which pins
    /// the same row — cannot attach a contract to a reservation that is being released. The
    /// linked CONTRACTS row is read BEFORE the reservation lock is taken, keeping the lock
    /// order CONTRACTS -&gt; GRID_CAPACITY_RESERVATIONS that ContractService uses in
    /// <c>CreateAsync</c> / <c>ReviewAsync</c> / <c>TerminateAsync</c>, so the two can never
    /// deadlock.
    /// </para>
    /// </summary>
    public async Task<GridCapacityReservationDto?> ReleaseAsync(int id, string? reason)
    {
        // Unlocked probe: decides which CONTRACTS row (if any) must be inspected before the
        // reservation lock is taken, and tells a missing row (404) apart from a reservation that
        // simply has no contract yet. AsNoTracking keeps this read out of the identity map so the
        // locked read below still observes the committed row.
        var probe = await _context.GridCapacityReservations
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.ContractId, r.ReleasedAt })
            .FirstOrDefaultAsync();

        if (probe is null) return null;

        // Idempotency (unchanged behaviour): a reservation that is already released is returned
        // as-is. Its ReleasedAt/ReleaseReason audit fields are never touched again, whichever
        // contract it may once have belonged to, so this short-circuit stays ahead of the
        // Decision C guard exactly as it was before the concurrency hardening.
        if (probe.ReleasedAt != null) return await GetByIdAsync(id);

        // Decision C: a live contract owns its reservation, so releasing it manually would free
        // grid capacity while the project is still live. This reuses the same "live contract"
        // definition as ContractService.CreateAsync. A null ContractId (accepted offer with no
        // contract yet) and a contract that has already ended (Rejected/Terminated) stay
        // releasable: a terminal contract can never return to a live status, so this verdict
        // cannot go stale while the lock below is being taken.
        if (probe.ContractId is { } contractId)
        {
            var isLiveContract = await _context.Contracts.AnyAsync(c =>
                c.Id == contractId &&
                (c.Status == ContractStatus.PendingSignatures || c.Status == ContractStatus.Active));

            if (isLiveContract) throw new BusinessRuleException(LiveContractReleaseMessage);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Pin the row for the rest of the transaction (same convention as OfferService,
        // LandService and ContractService). A concurrent release blocks here; a concurrent
        // contract creation blocks here before it can attach itself to this reservation.
        var reservation = await _context.GridCapacityReservations
            .FromSqlInterpolated($"SELECT * FROM GRID_CAPACITY_RESERVATIONS WITH (UPDLOCK, HOLDLOCK) WHERE ID = {id}")
            .FirstOrDefaultAsync();

        if (reservation is null)
        {
            await transaction.RollbackAsync();
            return null;
        }

        // Another release won the race: keep the FIRST release's audit trail untouched.
        if (reservation.ReleasedAt != null)
        {
            await transaction.RollbackAsync();
            return reservation.Adapt<GridCapacityReservationDto>();
        }

        // A contract attached itself to this reservation after the probe. The workflow only ever
        // attaches a PendingSignatures contract, and a rejected/terminated one would have released
        // this reservation (handled above), so the new link is live: refuse.
        if (reservation.ContractId != probe.ContractId)
        {
            await transaction.RollbackAsync();
            throw new BusinessRuleException(LiveContractReleaseMessage);
        }

        reservation.ReleasedAt = DateTime.UtcNow;
        reservation.ReleaseReason = string.IsNullOrWhiteSpace(reason)
            ? DefaultReleaseReason
            : (reason.Length <= ReleaseReasonMaxLength ? reason : reason[..ReleaseReasonMaxLength]);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return reservation.Adapt<GridCapacityReservationDto>();
    }
}
