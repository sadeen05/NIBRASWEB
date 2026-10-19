using System.ComponentModel.DataAnnotations;
using NibrasWeb.Enums;

namespace NIBRAS.API.DTOs;

public class CreateOfferRequest
{
    public int LandId { get; set; }

    /// <summary>Grid capacity requested for the project (MW). Must be greater than zero.</summary>
    [Range(0.01, 99999999.99, ErrorMessage = "Required capacity must be greater than zero.")]
    public decimal RequiredCapacityMw { get; set; }

    [EnumDataType(typeof(ConnectionMechanism), ErrorMessage = "ConnectionMechanism is not a defined value.")]
    public ConnectionMechanism ConnectionMechanism { get; set; }

    /// <summary>Proposed landlord share (percent). This is the offer amount and must be greater than zero.</summary>
    [Required]
    [Range(0.01, 999.99, ErrorMessage = "Landlord share (amount) must be greater than zero.")]
    public decimal? LandlordSharePct { get; set; }

    [Range(1, 100)]
    public int? DurationYears { get; set; }

    public DateOnly? StartDate { get; set; }

    [Range(0.01, 999999999999.99)]
    public decimal? InstallationCost { get; set; }

    [Range(0.01, 99999999.99)]
    public decimal? SolarCellCapacityKw { get; set; }

    [MaxLength(500)]
    public string? Message { get; set; }
}