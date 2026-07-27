namespace NIBRAS.API.DTOs;

public class LandDto
{
    public int Id { get; set; }
    public int LandlordId { get; set; }
    public int RegionId { get; set; }
    public string LandNumber { get; set; } = "";
    public decimal AreaDonum { get; set; }
    public decimal SlopePercentage { get; set; }
    public decimal DistanceToGridKm { get; set; }
    public decimal SolarIrradiance { get; set; }
    public decimal ElevationM { get; set; }
    public bool IsDeleted { get; set; }
    public int LandStatusId { get; set; }
    public bool DataVerifiedByAdmin { get; set; }
}
