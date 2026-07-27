using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
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
        var reservations = await _reservationService.GetAllAsync();
        return Ok(reservations);
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

    [HttpPost]
    public async Task<ActionResult<GridCapacityReservationDto>> Create(CreateGridCapacityReservationRequest request)
    {
        var reservation = await _reservationService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = reservation.Id }, reservation);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateGridCapacityReservationRequest request)
    {
        var result = await _reservationService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _reservationService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
