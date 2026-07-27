namespace NIBRAS.API.DTOs;

public class UpdateTariffBracketRequest
{
    public int RegionId { get; set; }
    public int FromKwh { get; set; }
    public int? ToKwh { get; set; }
    public decimal RatePerKwh { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}
