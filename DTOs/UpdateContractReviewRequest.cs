namespace NIBRAS.API.DTOs;

public class UpdateContractReviewRequest
{
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
}
