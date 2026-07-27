namespace NIBRAS.API.DTOs;

public class CreateOfferVersionRequest
{
    public int OfferId { get; set; }
    public int VersionNumber { get; set; }
    public decimal? LandlordSharePct { get; set; }
    public int? DurationYears { get; set; }
    public DateOnly? StartDate { get; set; }
    public decimal? InstallationCost { get; set; }
    public decimal? SolarCellCapacityKw { get; set; }
    public int CreatedById { get; set; }
}
