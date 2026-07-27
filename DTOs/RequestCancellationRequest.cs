namespace NIBRAS.API.DTOs;

public class RequestCancellationRequest
{
    public int UserId { get; set; }
    public string Reason { get; set; } = "";
    public decimal? InvestorPenaltyAmount { get; set; }
}
