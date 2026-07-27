namespace NIBRAS.API.DTOs;

public class ForceTerminateRequest
{
    public int AdminId { get; set; }
    public string Justification { get; set; } = "";
    public decimal? CompensationOverride { get; set; }
}
