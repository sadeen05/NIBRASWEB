namespace NIBRAS.API.DTOs;

public class UpdateLandStatusHistoryRequest
{
    public int StatusId { get; set; }
    public string? Reason { get; set; }
}
