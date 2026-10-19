using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

/// <summary>
/// Landlord change requests on an already-approved land. Creation is owner-only and
/// SuperAdmin is enforced in the service; a dedicated route prefix keeps these
/// endpoints from colliding with the <c>/api/Lands/{id}</c> routes.
/// </summary>
[ApiController]
[Route("api/land-change-requests")]
[Authorize]
public class LandChangeRequestsController : ControllerBase
{
    private readonly ILandService _landService;

    public LandChangeRequestsController(ILandService landService)
    {
        _landService = landService;
    }

    /// <summary>Landlord submits a change request; the land is hidden from investors until it is decided.</summary>
    [HttpPost]
    public async Task<ActionResult<LandChangeRequestDto>> Create(CreateLandChangeRequestRequest request)
    {
        var created = await _landService.SubmitChangeRequestAsync(request);
        // There is no GET-by-id for a change request; the resolvable location of the created
        // resource is the per-land collection it is listed in, which a caller can GET.
        // CreatedAtAction cannot be used here because the Create action has no {id} route value,
        // so it built a location that did not resolve back to the resource.
        return Created($"/api/land-change-requests?landId={created.LandId}", created);
    }

    /// <summary>Change requests of one land: its owner or Admin/SuperAdmin.</summary>
    [HttpGet]
    public async Task<ActionResult<List<LandChangeRequestDto>>> GetForLand([FromQuery] int landId)
    {
        return Ok(await _landService.GetChangeRequestsAsync(landId));
    }

    /// <summary>The SuperAdmin review queue (pending change requests only).</summary>
    [HttpGet("pending")]
    public async Task<ActionResult<List<LandChangeRequestDto>>> GetPending()
    {
        return Ok(await _landService.GetPendingChangeRequestsAsync());
    }

    /// <summary>SuperAdmin approve/reject decision.</summary>
    [HttpPost("{id}/decision")]
    public async Task<ActionResult<LandChangeRequestDto>> Decide(
        int id, ReviewLandChangeRequestRequest request)
    {
        return Ok(await _landService.DecideChangeRequestAsync(id, request));
    }
}
