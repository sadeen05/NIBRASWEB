namespace NIBRAS.API.DTOs;

public class CreateLandStatusHistoryRequest
{
    public int LandId { get; set; }
    public int StatusId { get; set; }
    public int ChangedById { get; set; }
    public string? Reason { get; set; }
}
