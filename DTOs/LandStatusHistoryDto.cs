namespace NIBRAS.API.DTOs;

public class LandStatusHistoryDto
{
    public int Id { get; set; }
    public int LandId { get; set; }
    public int StatusId { get; set; }
    public int ChangedById { get; set; }
    public string? Reason { get; set; }
    public DateTime? ChangedAt { get; set; }
}
