using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LandStatusesController : ControllerBase
{
    private readonly ILandStatusService _landStatusService;

    public LandStatusesController(ILandStatusService landStatusService)
    {
        _landStatusService = landStatusService;
    }

    [HttpGet]
    public async Task<ActionResult<List<LandStatusDto>>> GetAll()
    {
        var statuses = await _landStatusService.GetAllAsync();
        return Ok(statuses);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LandStatusDto>> GetById(int id)
    {
        var status = await _landStatusService.GetByIdAsync(id);

        if (status == null)
        {
            return NotFound();
        }

        return Ok(status);
    }

    [HttpPost]
    public async Task<ActionResult<LandStatusDto>> Create(CreateLandStatusRequest request)
    {
        var status = await _landStatusService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = status.Id }, status);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateLandStatusRequest request)
    {
        var result = await _landStatusService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _landStatusService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
