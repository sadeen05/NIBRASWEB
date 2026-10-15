using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public class CounterOfferRequest
{
    /// <summary>New proposed landlord share (percent). Must be greater than zero.</summary>
    [Range(0.01, 999.99, ErrorMessage = "Counter amount must be greater than zero.")]
    public decimal NewAmount { get; set; }

    [MaxLength(500)]
    public string? Message { get; set; }
}