namespace NIBRAS.API.DTOs;

public class AdminReviewRequest
{
    public int AdminId { get; set; }
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
}
