using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

/// <summary>
/// Read-only governorate lookup. Authentication is required (the global fallback
/// policy); there is no write route because the twelve governorates are fixed
/// seeded reference data.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GovernoratesController : ControllerBase
{
    private readonly IGovernorateService _governorateService;

    public GovernoratesController(IGovernorateService governorateService)
    {
        _governorateService = governorateService;
    }

    [HttpGet]
    public async Task<ActionResult<List<GovernorateDto>>> GetAll()
    {
        return Ok(await _governorateService.GetAllAsync());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GovernorateDto>> GetById(int id)
    {
        var governorate = await _governorateService.GetByIdAsync(id);

        if (governorate == null)
        {
            return NotFound();
        }

        return Ok(governorate);
    }
}
