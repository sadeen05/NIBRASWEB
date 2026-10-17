namespace NIBRAS.API.DTOs;

public class OfferVersionDto
{
    public int Id { get; set; }
    public int OfferId { get; set; }
    public int VersionNumber { get; set; }
    public decimal? LandlordSharePct { get; set; }
    public int? DurationYears { get; set; }
    public DateOnly? StartDate { get; set; }
    public decimal? InstallationCost { get; set; }
    public string CreatedByName { get; set; }
    public string CreatedByRole { get; set; }
    public decimal? SolarCellCapacityKw { get; set; }
    public decimal? ExpectedAnnualRevenue { get; set; }
    public decimal? EffectiveCostPerKw { get; set; }
    public decimal? PaybackPeriodMonths { get; set; }
    public string? Message { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime CreatedAt { get; set; }
}
