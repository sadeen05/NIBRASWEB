namespace NIBRAS.API.DTOs;

public class CreateOfferRequest
{
    public int LandId { get; set; }
    public int InvestorId { get; set; }
    public decimal RequiredCapacityMw { get; set; }
    public int StatusId { get; set; }
}
