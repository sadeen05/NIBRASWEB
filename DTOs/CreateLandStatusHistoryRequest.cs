using NIBRAS.Models;

namespace NIBRAS.API.DTOs;

public class CreateLandStatusHistoryRequest
{
    public int LandId { get; set; }
    public LandStatus Status { get; set; }
    public int ChangedById { get; set; }
    public string? Reason { get; set; }
}
