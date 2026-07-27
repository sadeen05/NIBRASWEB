namespace NIBRAS.API.DTOs;

public class ContractDto
{
    public int Id { get; set; }
    public int OfferId { get; set; }
    public int LandId { get; set; }
    public int InvestorId { get; set; }
    public int LandlordId { get; set; }
    public int StatusId { get; set; }
    public DateTime? InvestorSignedAt { get; set; }
    public DateTime? AdminReviewedAt { get; set; }
    public DateTime? LandlordSignedAt { get; set; }
    public DateTime? AdminSignedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int? OfferVersionId { get; set; }
    public int NoticePeriodDays { get; set; }
    public int? CancellationRequestedById { get; set; }
    public DateTime? CancellationRequestedAt { get; set; }
    public DateTime? CancellationEffectiveDate { get; set; }
    public decimal? InvestorPenaltyAmount { get; set; }
    public bool DisputeFlagged { get; set; }
    public decimal? CompensationAmount { get; set; }
}
