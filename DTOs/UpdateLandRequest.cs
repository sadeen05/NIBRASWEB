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

    // Upper bound matches the LANDS.AREA_DONUM column, decimal(10,2) (SEC-14 / 4.10).
    [Range(typeof(decimal), "0.0001", "99999999.99")]
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
