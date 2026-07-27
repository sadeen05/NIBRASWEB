using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OfferStatusesController : ControllerBase
{
    private readonly IOfferStatusService _offerStatusService;

    public OfferStatusesController(IOfferStatusService offerStatusService)
    {
        _offerStatusService = offerStatusService;
    }

    [HttpGet]
    public async Task<ActionResult<List<OfferStatusDto>>> GetAll()
    {
        var statuses = await _offerStatusService.GetAllAsync();
        return Ok(statuses);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OfferStatusDto>> GetById(int id)
    {
        var status = await _offerStatusService.GetByIdAsync(id);

        if (status == null)
        {
            return NotFound();
        }

        return Ok(status);
    }

    [HttpPost]
    public async Task<ActionResult<OfferStatusDto>> Create(CreateOfferStatusRequest request)
    {
        var status = await _offerStatusService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = status.Id }, status);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateOfferStatusRequest request)
    {
        var result = await _offerStatusService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _offerStatusService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
