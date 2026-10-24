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

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<List<ContractVersionDto>>> GetVersions(int id)
    {
        return Ok(await _contractService.GetVersionsAsync(id));
    }

    [HttpGet("{id}/revisions")]
    public async Task<ActionResult<List<ContractRevisionDto>>> GetRevisions(int id)
    {
        return Ok(await _contractService.GetRevisionsAsync(id));
    }

    [HttpPost("{id}/revisions")]
    public async Task<ActionResult<ContractRevisionDto>> RequestRevision(
        int id,
        CreateContractRevisionRequest request)
    {
        var revision = await _contractService.RequestRevisionAsync(id, request);
        return CreatedAtAction(nameof(GetRevisions), new { id }, revision);
    }

    [HttpPost("{id}/revisions/{revisionId}/consent")]
    public async Task<ActionResult<ContractRevisionDto>> ConsentToRevision(
        int id,
        int revisionId,
        ContractRevisionConsentRequest request)
    {
        return Ok(await _contractService.ConsentToRevisionAsync(id, revisionId, request));
    }

    [HttpPost("{id}/revisions/{revisionId}/admin-decision")]
    public async Task<ActionResult<ContractRevisionDto>> AdminDecideRevision(
        int id,
        int revisionId,
        ContractRevisionAdminDecisionRequest request)
    {
        return Ok(await _contractService.AdminReviewRevisionAsync(id, revisionId, request));
    }

    [HttpPost("{id}/revisions/{revisionId}/superadmin-decision")]
    public async Task<ActionResult<ContractRevisionDto>> SuperAdminDecideRevision(
        int id,
        int revisionId,
        ContractRevisionSuperAdminDecisionRequest request)
    {
        return Ok(await _contractService.SuperAdminDecideRevisionAsync(id, revisionId, request));
    }
}
