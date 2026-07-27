using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GridsController : ControllerBase
{
    private readonly IGridService _gridService;

    public GridsController(IGridService gridService)
    {
        _gridService = gridService;
    }

    [HttpGet]
    public async Task<ActionResult<List<GridDto>>> GetAll()
    {
        var grids = await _gridService.GetAllAsync();
        return Ok(grids);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GridDto>> GetById(int id)
    {
        var grid = await _gridService.GetByIdAsync(id);

        if (grid == null)
        {
            return NotFound();
        }

        return Ok(grid);
    }

    [HttpPost]
    public async Task<ActionResult<GridDto>> Create(CreateGridRequest request)
    {
        var grid = await _gridService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = grid.Id }, grid);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateGridRequest request)
    {
        var result = await _gridService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _gridService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
