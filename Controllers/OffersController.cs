using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NibrasWeb.DTOs;
using NibrasWeb.Service;

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

    [HttpGet("land/{landId}/user/{requestingUserId}")]
    public async Task<ActionResult<List<OfferDto>>> GetForLand(int landId, int requestingUserId)
    {
        var offers = await _offerService.GetOffersForLandAsync(landId, requestingUserId);
        return Ok(offers);
    }

    [HttpGet("investor/{investorId}")]
    public async Task<ActionResult<List<OfferDto>>> GetForInvestor(int investorId, [FromQuery] string? statusFilter = null)
    {
        var offers = await _offerService.GetOfferDtosAsync(investorId, statusFilter);
        return Ok(offers);
    }

    [HttpGet("pending-admin-approval")]
    public async Task<ActionResult<List<OfferDto>>> GetPendingAdminApproval()
    {
        var offers = await _offerService.GetPendingAdminApprovalAsync();
        return Ok(offers);
    }

    [HttpPost]
    public async Task<ActionResult<OfferDto>> Create(CreateOfferRequest request)
    {
        var offer = await _offerService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = offer.Id }, offer);
    }

    [HttpPost("{id}/counter-offer")]
    public async Task<ActionResult<OfferDto>> CounterOffer(int id, [FromBody] CounterOfferRequest request)
    {
        var offer = await _offerService.CounterOfferAsync(id, request.UserId, request.NewAmount, request.Message);
        return Ok(offer);
    }

    [HttpPost("{id}/accept")]
    public async Task<ActionResult<OfferDto>> Accept(int id, [FromBody] UserActionRequest request)
    {
        var offer = await _offerService.AcceptAsync(id, request.UserId);
        return Ok(offer);
    }

    [HttpPost("{id}/reject")]
    public async Task<ActionResult<OfferDto>> Reject(int id, [FromBody] RejectOfferRequest request)
    {
        var offer = await _offerService.RejectAsync(id, request.UserId, request.Reason);
        return Ok(offer);
    }

    [HttpPost("{id}/withdraw")]
    public async Task<ActionResult<OfferDto>> Withdraw(int id, [FromBody] UserActionRequest request)
    {
        var offer = await _offerService.WithdrawAsync(id, request.UserId);
        return Ok(offer);
    }

    [HttpPost("{id}/admin-approve")]
    public async Task<ActionResult<OfferDto>> AdminApprove(int id, [FromBody] UserActionRequest request)
    {
        var offer = await _offerService.AdminApproveAsync(id, request.UserId);
        return Ok(offer);
    }

    [HttpPost("{id}/admin-reject")]
    public async Task<ActionResult<OfferDto>> AdminReject(int id, [FromBody] RejectOfferRequest request)
    {
        var offer = await _offerService.AdminRejectAsync(id, request.UserId, request.Reason);
        return Ok(offer);
    }

    [HttpGet("{id}/negotiation-history")]
    public async Task<ActionResult<List<OfferNegotiationHistoryDto>>> GetNegotiationHistory(int id, [FromQuery] int requestingUserId)
    {
        var history = await _offerService.GetNegotiationHistoryAsync(id, requestingUserId);
        return Ok(history);
    }

    [HttpPost("{id}/close")]
    public async Task<IActionResult> CloseByContract(int id, [FromBody] CloseOfferRequest request)
    {
        await _offerService.MarkOfferClosedAsync(id, request.ContractId, request.UserId);
        return NoContent();
    }
}
d8