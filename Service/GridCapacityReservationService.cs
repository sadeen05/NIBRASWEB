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
    /// <c>ReleasedAt</c> and <c>ReleaseReason</c> are never overwritten, so a repeated
    /// release cannot release it twice or alter the original audit trail.
    /// A reservation owned by a LIVE contract (<c>PendingSignatures</c> or <c>Active</c>)
    /// cannot be released here — the contract workflow releases it on reject/terminate.
    /// </summary>
    public async Task<GridCapacityReservationDto?> ReleaseAsync(int id, string? reason)
    {
        var reservation = await _context.GridCapacityReservations.FirstOrDefaultAsync(r => r.Id == id);
        if (reservation == null) return null;

        if (reservation.ReleasedAt != null) return reservation.Adapt<GridCapacityReservationDto>();

        // Decision C: a live contract owns its reservation, so releasing it manually would free
        // grid capacity while the project is still live. This reuses the same "live contract"
        // definition as ContractService.CreateAsync. A null ContractId (accepted offer with no
        // contract yet) and a contract that has already ended (Rejected/Terminated) stay releasable.
        if (reservation.ContractId is { } contractId)
        {
            var isLiveContract = await _context.Contracts.AnyAsync(c =>
                c.Id == contractId &&
                (c.Status == ContractStatus.PendingSignatures || c.Status == ContractStatus.Active));

            if (isLiveContract) throw new BusinessRuleException(LiveContractReleaseMessage);
        }

        reservation.ReleasedAt = DateTime.UtcNow;
        reservation.ReleaseReason = string.IsNullOrWhiteSpace(reason)
            ? DefaultReleaseReason
            : (reason.Length <= ReleaseReasonMaxLength ? reason : reason[..ReleaseReasonMaxLength]);

        await _context.SaveChangesAsync();
        return reservation.Adapt<GridCapacityReservationDto>();
    }
}
