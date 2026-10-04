using NibrasWeb.Enums;

namespace NIBRAS.API.DTOs;

public class CreateOfferRequest
{
    public int LandId { get; set; }
    public int InvestorId { get; set; }
    public decimal RequiredCapacityMw { get; set; }
    public ConnectionMechanism ConnectionMechanism { get; set; }
    public decimal? LandlordSharePct { get; set; }
    public int? DurationYears { get; set; }
    public DateOnly? StartDate { get; set; }
    public decimal? InstallationCost { get; set; }
    public decimal? SolarCellCapacityKw { get; set; }
    public string? Message { get; set; }
}
