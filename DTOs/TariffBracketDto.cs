namespace NIBRAS.API.DTOs;

public class TariffBracketDto
{
    public int Id { get; set; }
    public int RegionId { get; set; }
    public int FromKwh { get; set; }
    public int? ToKwh { get; set; }
    public decimal RatePerKwh { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}
