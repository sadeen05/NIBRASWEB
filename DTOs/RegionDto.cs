namespace NIBRAS.API.DTOs;

public class RegionDto
{
    public int Id { get; set; }
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public decimal PeakSunHoursPerDay { get; set; }
    public decimal WheelingFeePerKwh { get; set; }
    public decimal LossPercentage { get; set; }
}
