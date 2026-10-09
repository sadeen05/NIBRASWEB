using NIBRAS.Models;

namespace NIBRAS.API.DTOs;

public class UpdateLandStatusHistoryRequest
{
    public LandStatus Status { get; set; }
    public string? Reason { get; set; }
}
