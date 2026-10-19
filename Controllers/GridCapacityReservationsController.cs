using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;

namespace NIBRAS.API.Controllers;

/// <summary>
/// Admin read access plus the Admin release operation (BR-G3).
/// Reservations are created only by workflow code, so there is deliberately no
/// public create/update/delete route here.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.SuperAdmin)]
public class GridCapacityReservationsController : ControllerBase
{
    private readonly IGridCapacityReservationService _reservationService;

    public GridCapacityReservationsController(IGridCapacityReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    [HttpGet]
    public async Task<ActionResult<List<GridCapacityReservationDto>>> GetAll()
    {
        return Ok(await _reservationService.GetAllAsync());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GridCapacityReservationDto>> GetById(int id)
    {
        var reservation = await _reservationService.GetByIdAsync(id);

        if (reservation == null)
        {
            return NotFound();
        }

        return Ok(reservation);
    }

    /// <summary>
    /// Releases a reservation without deleting it. The body is optional
    /// (<c>{ "reason": "..." }</c>); repeating the call is idempotent and never
    /// overwrites the original release.
    /// </summary>
    [HttpPost("{id}/release")]
    public async Task<ActionResult<GridCapacityReservationDto>> Release(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReleaseGridCapacityReservationRequest? request)
    {
        var reservation = await _reservationService.ReleaseAsync(id, request?.Reason);

        if (reservation == null)
        {
            return NotFound();
        }

        return Ok(reservation);
    }
}
