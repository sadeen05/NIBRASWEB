using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContractStatusesController : ControllerBase
{
    private readonly IContractStatusService _contractStatusService;

    public ContractStatusesController(IContractStatusService contractStatusService)
    {
        _contractStatusService = contractStatusService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ContractStatusDto>>> GetAll()
    {
        var statuses = await _contractStatusService.GetAllAsync();
        return Ok(statuses);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ContractStatusDto>> GetById(int id)
    {
        var status = await _contractStatusService.GetByIdAsync(id);

        if (status == null)
        {
            return NotFound();
        }

        return Ok(status);
    }

    [HttpPost]
    public async Task<ActionResult<ContractStatusDto>> Create(CreateContractStatusRequest request)
    {
        var status = await _contractStatusService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = status.Id }, status);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateContractStatusRequest request)
    {
        var result = await _contractStatusService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _contractStatusService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
