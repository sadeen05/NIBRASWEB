using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/criteria")]
[Authorize]
public class CriteriaController : ControllerBase
{
    private readonly ICriteriaService _criteriaService;

    public CriteriaController(ICriteriaService criteriaService)
    {
        _criteriaService = criteriaService;
    }

    [HttpGet("current")]
    public async Task<ActionResult<LandCriterionDto>> GetCurrent()
    {
        return Ok(await _criteriaService.GetCurrentAsync());
    }

    [HttpGet("history")]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.SuperAdmin)]
    public async Task<ActionResult<List<LandCriterionDto>>> GetHistory()
    {
        return Ok(await _criteriaService.GetHistoryAsync());
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Admin + "," + RoleNames.SuperAdmin)]
    public async Task<ActionResult<LandCriterionDto>> Create(CreateLandCriterionRequest request)
    {
        var criterion = await _criteriaService.CreateAsync(request);
        return StatusCode(StatusCodes.Status201Created, criterion);
    }
}
