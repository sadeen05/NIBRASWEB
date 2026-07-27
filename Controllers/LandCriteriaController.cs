using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LandCriteriaController : ControllerBase
{
    private readonly ILandCriterionService _landCriterionService;

    public LandCriteriaController(ILandCriterionService landCriterionService)
    {
        _landCriterionService = landCriterionService;
    }

    [HttpGet]
    public async Task<ActionResult<List<LandCriterionDto>>> GetAll()
    {
        var criteria = await _landCriterionService.GetAllAsync();
        return Ok(criteria);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LandCriterionDto>> GetById(int id)
    {
        var criterion = await _landCriterionService.GetByIdAsync(id);

        if (criterion == null)
        {
            return NotFound();
        }

        return Ok(criterion);
    }

    [HttpPost]
    public async Task<ActionResult<LandCriterionDto>> Create(CreateLandCriterionRequest request)
    {
        var criterion = await _landCriterionService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = criterion.Id }, criterion);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateLandCriterionRequest request)
    {
        var result = await _landCriterionService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _landCriterionService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
