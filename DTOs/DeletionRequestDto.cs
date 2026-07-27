namespace NIBRAS.API.DTOs;

public class DeletionRequestDto
{
    public int Id { get; set; }
    public int LandId { get; set; }
    public int RequestedById { get; set; }
    public string Reason { get; set; } = "";
    public string? Status { get; set; }
    public string? AdminComment { get; set; }
    public DateTime? CreatedAt { get; set; }
}
