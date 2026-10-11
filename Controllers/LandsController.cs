using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LandsController : ControllerBase
{
    private readonly ILandService _landService;

    public LandsController(ILandService landService)
    {
        _landService = landService;
    }

    [HttpGet]
    public async Task<ActionResult<LandListResponse>> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        return Ok(await _landService.GetAllAsync(pageNumber, pageSize, status));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LandDto>> GetById(int id)
    {
        return Ok(await _landService.GetByIdAsync(id));
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
        await _landService.UpdateAsync(id, request);
        return NoContent();
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> Submit(int id)
    {
        await _landService.SubmitAsync(id);
        return Ok(new { message = "Land submitted for verification." });
    }

    [HttpPost("{id}/revise")]
    public async Task<IActionResult> Revise(int id)
    {
        await _landService.ReviseAsync(id);
        return Ok(new { message = "Land returned to Draft." });
    }

    [HttpPost("{id}/verify")]
    public async Task<IActionResult> Verify(int id)
    {
        await _landService.VerifyAsync(id);
        return Ok(new { message = "Land verified successfully." });
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(int id, [FromQuery] string reason)
    {
        await _landService.RejectAsync(id, reason);
        return Ok(new { message = "Land rejected." });
    }

    [HttpGet("{id}/eligibility")]
    public async Task<ActionResult<LandEligibilityResponse>> GetEligibility(int id)
    {
        return Ok(await _landService.GetEligibilityAsync(id));
    }

    [HttpGet("{id}/history")]
    public async Task<ActionResult<List<LandStatusHistoryDto>>> GetHistory(int id)
    {
        return Ok(await _landService.GetAllStatusHistoryAsync(id));
    }
}
