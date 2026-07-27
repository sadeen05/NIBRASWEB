using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeletionRequestsController : ControllerBase
{
    private readonly IDeletionRequestService _deletionRequestService;

    public DeletionRequestsController(IDeletionRequestService deletionRequestService)
    {
        _deletionRequestService = deletionRequestService;
    }

    [HttpGet]
    public async Task<ActionResult<List<DeletionRequestDto>>> GetAll()
    {
        var requests = await _deletionRequestService.GetAllAsync();
        return Ok(requests);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DeletionRequestDto>> GetById(int id)
    {
        var request = await _deletionRequestService.GetByIdAsync(id);

        if (request == null)
        {
            return NotFound();
        }

        return Ok(request);
    }

    [HttpPost]
    public async Task<ActionResult<DeletionRequestDto>> Create(CreateDeletionRequestRequest request)
    {
        var deletionRequest = await _deletionRequestService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = deletionRequest.Id }, deletionRequest);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateDeletionRequestRequest request)
    {
        var result = await _deletionRequestService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _deletionRequestService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
