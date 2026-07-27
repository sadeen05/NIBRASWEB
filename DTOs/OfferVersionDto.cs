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
    public int CreatedById { get; set; }
    public string? RejectionReason { get; set; }
    public decimal? SolarCellCapacityKw { get; set; }
    public decimal? ExpectedAnnualRevenue { get; set; }
    public decimal? EffectiveCostPerKw { get; set; }
    public decimal? PaybackPeriodMonths { get; set; }
    public DateTime? CreatedAt { get; set; }
}
