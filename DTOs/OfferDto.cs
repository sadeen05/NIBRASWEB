using NibrasWeb.Enums;

namespace NIBRAS.API.DTOs;

public class OfferDto
{
    public int Id { get; set; }
    public int LandId { get; set; }
    public int InvestorId { get; set; }
    public OfferStatus Status { get; set; }
    public ConnectionMechanism ConnectionMechanism { get; set; }
    public decimal CurrentAmount { get; set; }
    public int LastProposedById { get; set; }
    public decimal RequiredCapacityMw { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? RelatedToRejectedOfferId { get; set; }
    public string? InvestorName { get; set; }
    public string? LandLandlordName { get; set; }
    public OfferVersionDto? LatestVersion { get; set; }
}
