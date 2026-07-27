namespace NIBRAS.API.DTOs;

public class CreateLandRequest
{
    public int LandlordId { get; set; }
    public int RegionId { get; set; }
    public string LandNumber { get; set; } = "";
    public decimal AreaDonum { get; set; }
    public decimal SlopePercentage { get; set; }
    public decimal DistanceToGridKm { get; set; }
    public decimal SolarIrradiance { get; set; }
    public decimal ElevationM { get; set; }
}
