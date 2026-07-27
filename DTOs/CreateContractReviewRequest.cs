namespace NIBRAS.API.DTOs;

public class CreateContractReviewRequest
{
    public int ContractId { get; set; }
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
}
