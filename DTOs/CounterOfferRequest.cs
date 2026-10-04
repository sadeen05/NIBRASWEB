namespace NIBRAS.API.DTOs;

public class CounterOfferRequest
{
    public int UserId { get; set; }
    public decimal NewAmount { get; set; }
    public string? Message { get; set; }
}
