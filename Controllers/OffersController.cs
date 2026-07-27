using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OffersController : ControllerBase
{
    private readonly IOfferService _offerService;

    public OffersController(IOfferService offerService)
    {
        _offerService = offerService;
    }

    [HttpGet]
    public async Task<ActionResult<List<OfferDto>>> GetAll()
    {
        var offers = await _offerService.GetAllAsync();
        return Ok(offers);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<OfferDto>> GetById(int id)
    {
        var offer = await _offerService.GetByIdAsync(id);

        if (offer == null)
        {
            return NotFound();
        }

        return Ok(offer);
    }

    [HttpPost]
    public async Task<ActionResult<OfferDto>> Create(CreateOfferRequest request)
    {
        var offer = await _offerService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = offer.Id }, offer);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateOfferRequest request)
    {
        var result = await _offerService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _offerService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
