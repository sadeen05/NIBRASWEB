using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegionsController : ControllerBase
{
    private readonly IRegionService _regionService;

    public RegionsController(IRegionService regionService)
    {
        _regionService = regionService;
    }

    [HttpGet]
    public async Task<ActionResult<List<RegionDto>>> GetAll()
    {
        var regions = await _regionService.GetAllAsync();
        return Ok(regions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RegionDto>> GetById(int id)
    {
        var region = await _regionService.GetByIdAsync(id);

        if (region == null)
        {
            return NotFound();
        }

        return Ok(region);
    }

    [HttpPost]
    public async Task<ActionResult<RegionDto>> Create(CreateRegionRequest request)
    {
        var region = await _regionService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = region.Id }, region);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateRegionRequest request)
    {
        var result = await _regionService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _regionService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
