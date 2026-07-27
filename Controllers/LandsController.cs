using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LandsController : ControllerBase
{
    private readonly ILandService _landService;

    public LandsController(ILandService landService)
    {
        _landService = landService;
    }

    [HttpGet]
    public async Task<ActionResult<List<LandDto>>> GetAll()
    {
        var lands = await _landService.GetAllAsync();
        return Ok(lands);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LandDto>> GetById(int id)
    {
        var land = await _landService.GetByIdAsync(id);

        if (land == null)
        {
            return NotFound();
        }

        return Ok(land);
    }

    [HttpPost]
    public async Task<ActionResult<LandDto>> Create(CreateLandRequest request)
    {
        var land = await _landService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = land.Id }, land);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateLandRequest request)
    {
        var result = await _landService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _landService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
