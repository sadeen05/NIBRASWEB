using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContractsController : ControllerBase
{
    private readonly IContractService _contractService;

    public ContractsController(IContractService contractService)
    {
        _contractService = contractService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ContractDto>>> GetAll()
    {
        var contracts = await _contractService.GetAllAsync();
        return Ok(contracts);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ContractDto>> GetById(int id)
    {
        var contract = await _contractService.GetByIdAsync(id);

        if (contract == null)
        {
            return NotFound();
        }

        return Ok(contract);
    }

    [HttpPost]
    public async Task<ActionResult<ContractDto>> Create(CreateContractRequest request)
    {
        var contract = await _contractService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = contract.Id }, contract);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateContractRequest request)
    {
        var result = await _contractService.UpdateAsync(id, request);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _contractService.DeleteAsync(id);

        if (result == false)
        {
            return NotFound();
        }

        return NoContent();
    }
}
