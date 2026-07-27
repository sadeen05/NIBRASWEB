namespace NIBRAS.API.DTOs;

public class ContractReviewDto
{
    public int Id { get; set; }
    public int ContractId { get; set; }
    public int ReviewerId { get; set; }
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime? CreatedAt { get; set; }
}
