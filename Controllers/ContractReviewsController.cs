using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContractReviewsController : ControllerBase
{
    private readonly IContractReviewService _contractReviewService;

    public ContractReviewsController(IContractReviewService contractReviewService)
    {
        _contractReviewService = contractReviewService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ContractReviewDto>>> GetAll()
    {
        var reviews = await _contractReviewService.GetAllAsync();
        return Ok(reviews);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ContractReviewDto>> GetById(int id)
    {
        var review = await _contractReviewService.GetByIdAsync(id);

        if (review == null)
        {
            return NotFound();
        }

        return Ok(review);
    }

    [HttpPost]
    public async Task<ActionResult<ContractReviewDto>> Create(CreateContractReviewRequest request)
    {
        var review = await _contractReviewService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = review.Id }, review);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateContractReviewRequest request)
    {
        var result = await _contractReviewService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _contractReviewService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
