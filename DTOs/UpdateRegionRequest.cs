namespace NIBRAS.API.DTOs;

public class UpdateRegionRequest
{
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public decimal PeakSunHoursPerDay { get; set; }
    public decimal WheelingFeePerKwh { get; set; }
    public decimal LossPercentage { get; set; }
}
