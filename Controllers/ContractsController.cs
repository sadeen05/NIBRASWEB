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
        if (contract == null) return NotFound();
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
        if (result == false) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _contractService.DeleteAsync(id);
        if (result == false) return NotFound();
        return NoContent();
    }

    // ============ SIGNING ============

    [HttpPost("{id}/sign/investor")]
    public async Task<IActionResult> SignAsInvestor(int id, SignContractRequest request)
    {
        var result = await _contractService.SignAsInvestorAsync(id, request.UserId);
        if (!result) return NotFound();
        return Ok(new { message = "Investor signature recorded." });
    }

    [HttpPost("{id}/sign/landlord")]
    public async Task<IActionResult> SignAsLandlord(int id, SignContractRequest request)
    {
        var result = await _contractService.SignAsLandlordAsync(id, request.UserId);
        if (!result) return NotFound();
        return Ok(new { message = "Landlord signature recorded." });
    }

    // ============ ADMIN REVIEW ============

    [HttpPost("{id}/review")]
    public async Task<ActionResult<ContractReviewDto>> Review(int id, AdminReviewRequest request)
    {
        var review = await _contractService.AdminReviewAsync(id, request.AdminId, request.Decision, request.Reason);
        return Ok(review);
    }

    // ============ CANCELLATION ============

    [HttpPost("{id}/cancellation/request")]
    public async Task<IActionResult> RequestCancellation(int id, RequestCancellationRequest request)
    {
        var result = await _contractService.RequestCancellationAsync(
            id, request.UserId, request.Reason, request.InvestorPenaltyAmount);
        if (!result) return NotFound();
        return Ok(new { message = "Cancellation requested." });
    }

    [HttpPost("{id}/cancellation/respond")]
    public async Task<IActionResult> RespondToCancellation(int id, RespondToCancellationRequest request)
    {
        var result = await _contractService.RespondToCancellationAsync(id, request.UserId, request.Agree);
        if (!result) return NotFound();
        return Ok(new { message = request.Agree ? "Cancellation agreed." : "Dispute flagged." });
    }

    // ============ TERMINATION ============

    [HttpPost("{id}/terminate")]
    public async Task<IActionResult> Terminate(int id, ForceTerminateRequest request)
    {
        var result = await _contractService.TerminateAsync(id, request.AdminId, request.Justification);
        if (!result) return NotFound();
        return Ok(new { message = "Contract terminated." });
    }

    [HttpPost("{id}/force-terminate")]
    public async Task<IActionResult> ForceTerminate(int id, ForceTerminateRequest request)
    {
        var result = await _contractService.AdminForceTerminateAsync(
            id, request.AdminId, request.Justification, request.CompensationOverride);
        if (!result) return NotFound();
        return Ok(new { message = "Contract force-terminated by admin." });
    }
}
