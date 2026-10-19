namespace NIBRAS.API.DTOs;

public class LandStatusHistoryDto
{
    public int id {  get; set; }
    public int LandId { get; set; }
    public string statusName { get; set; } = "";
    public string ChangeByName { get; set; } = "";
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
}
