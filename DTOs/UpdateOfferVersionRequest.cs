namespace NIBRAS.API.DTOs;

public class UpdateOfferVersionRequest
{
    public decimal? LandlordSharePct { get; set; }
    public int? DurationYears { get; set; }
    public DateOnly? StartDate { get; set; }
    public decimal? InstallationCost { get; set; }
    public string? RejectionReason { get; set; }
}
