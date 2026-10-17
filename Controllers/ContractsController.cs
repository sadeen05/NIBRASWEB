using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ContractsController : ControllerBase
{
    private readonly IContractService _contractService;

    public ContractsController(IContractService contractService)
    {
        _contractService = contractService;
    }

    [HttpGet]
    public async Task<ActionResult<ContractListResponse>> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        return Ok(await _contractService.GetAllAsync(pageNumber, pageSize, status));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ContractDto>> GetById(int id)
    {
        return Ok(await _contractService.GetByIdAsync(id));
    }

    [HttpPost]
    public async Task<ActionResult<ContractDto>> Create(CreateContractRequest request)
    {
        var contract = await _contractService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = contract.Id }, contract);
    }

    [HttpPost("{id}/sign")]
    public async Task<ActionResult<ContractDto>> Sign(int id)
    {
        return Ok(await _contractService.SignAsync(id));
    }

    [HttpPost("{id}/review")]
    public async Task<ActionResult<ContractDto>> Review(int id, ReviewContractRequest request)
    {
        return Ok(await _contractService.ReviewAsync(id, request));
    }

    [HttpPost("{id}/terminate")]
    public async Task<ActionResult<ContractDto>> Terminate(int id, TerminateContractRequest request)
    {
        return Ok(await _contractService.TerminateAsync(id, request));
    }

    [HttpGet("{id}/reviews")]
    public async Task<ActionResult<List<ContractReviewDto>>> GetReviews(int id)
    {
        return Ok(await _contractService.GetReviewsAsync(id));
    }
}
