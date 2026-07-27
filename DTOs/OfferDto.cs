namespace NIBRAS.API.DTOs;

public class OfferDto
{
    public int Id { get; set; }
    public int LandId { get; set; }
    public int InvestorId { get; set; }
    public decimal RequiredCapacityMw { get; set; }
    public int StatusId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
