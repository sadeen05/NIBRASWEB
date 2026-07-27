using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OfferVersionsController : ControllerBase
{
    private readonly IOfferVersionService _offerVersionService;

    public OfferVersionsController(IOfferVersionService offerVersionService)
    {
        _offerVersionService = offerVersionService;
    }

    [HttpGet]
    public async Task<ActionResult<List<OfferVersionDto>>> GetAll()
    {
        var versions = await _offerVersionService.GetAllAsync();
        return Ok(versions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OfferVersionDto>> GetById(int id)
    {
        var version = await _offerVersionService.GetByIdAsync(id);

        if (version == null)
        {
            return NotFound();
        }

        return Ok(version);
    }

    [HttpPost]
    public async Task<ActionResult<OfferVersionDto>> Create(CreateOfferVersionRequest request)
    {
        var version = await _offerVersionService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = version.Id }, version);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateOfferVersionRequest request)
    {
        var result = await _offerVersionService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _offerVersionService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
