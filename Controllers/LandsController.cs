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

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> Submit(int id)
    {
        var result = await _landService.SubmitAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Land submitted for verification." });
    }

    [HttpPost("{id}/verify")]
    public async Task<IActionResult> Verify(int id, [FromQuery] int adminId)
    {
        var result = await _landService.VerifyAsync(id, adminId);
        if (!result) return NotFound();
        return Ok(new { message = "Land verified successfully." });
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(int id, [FromQuery] int adminId, [FromQuery] string reason)
    {
        var result = await _landService.RejectAsync(id, adminId, reason);
        if (!result) return NotFound();
        return Ok(new { message = "Land rejected." });
    }
}
