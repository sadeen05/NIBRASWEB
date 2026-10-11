using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public class UpdateLandRequest
{
    [Required]
    public int RegionId { get; set; }

    [Required]
    public int GridId { get; set; }

    [Required, NoControlCharacters, MaxLength(50)]
    public string LandNumber { get; set; } = "";

    [Required, NoControlCharacters, MaxLength(100)]
    public string Village { get; set; } = "";

    [Required, NoControlCharacters, MaxLength(100)]
    public string Basin { get; set; } = "";

    [Required, NoControlCharacters, MaxLength(100)]
    public string ParcelNumber { get; set; } = "";

    [Range(typeof(decimal), "0.0001", "1000000000")]
    public decimal AreaDonum { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal SlopePercentage { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal DistanceToGridKm { get; set; }

    [Range(typeof(decimal), "0", "12")]
    public decimal SolarIrradiance { get; set; }

    [Range(typeof(decimal), "-1000", "10000")]
    public decimal ElevationM { get; set; }
}
