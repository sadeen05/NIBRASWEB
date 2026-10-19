using NIBRAS.API.DTOs;

namespace NIBRAS.API.Services;

/// <summary>
/// Read-only reservation access plus the Admin release operation (BR-G3).
/// Reservations are created only by workflow code; there is deliberately no
/// public create/update/delete surface.
/// </summary>
public interface IGridCapacityReservationService
{
    Task<List<GridCapacityReservationDto>> GetAllAsync();

    Task<GridCapacityReservationDto?> GetByIdAsync(int id);

    /// <summary>
    /// Releases a reservation by stamping <c>ReleasedAt</c> (never deleting it).
    /// Returns <c>null</c> when the reservation does not exist; an already-released
    /// reservation is returned unchanged.
    /// </summary>
    Task<GridCapacityReservationDto?> ReleaseAsync(int id, string? reason);
}
