using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LandStatusHistoryController : ControllerBase
{
    private readonly ILandStatusHistoryService _landStatusHistoryService;

    public LandStatusHistoryController(ILandStatusHistoryService landStatusHistoryService)
    {
        _landStatusHistoryService = landStatusHistoryService;
    }

    [HttpGet]
    public async Task<ActionResult<List<LandStatusHistoryDto>>> GetAll()
    {
        var records = await _landStatusHistoryService.GetAllAsync();
        return Ok(records);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LandStatusHistoryDto>> GetById(int id)
    {
        var record = await _landStatusHistoryService.GetByIdAsync(id);

        if (record == null)
        {
            return NotFound();
        }

        return Ok(record);
    }

    [HttpPost]
    public async Task<ActionResult<LandStatusHistoryDto>> Create(CreateLandStatusHistoryRequest request)
    {
        var record = await _landStatusHistoryService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = record.Id }, record);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateLandStatusHistoryRequest request)
    {
        var result = await _landStatusHistoryService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _landStatusHistoryService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
