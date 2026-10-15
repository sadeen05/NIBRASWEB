using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public class RejectOfferRequest
{
    [Required(ErrorMessage = "A rejection reason is required.")]
    [MaxLength(500, ErrorMessage = "Rejection reason must be at most 500 characters.")]
    public string Reason { get; set; } = string.Empty;
}