namespace NIBRAS.API.DTOs;

public class LandCriterionDto
{
    public int Id { get; set; }
    public decimal MinAreaDonum { get; set; }
    public decimal MaxSlopePct { get; set; }
    public decimal MaxGridDistanceKm { get; set; }
    public decimal MinSolarIrradiance { get; set; }
    public decimal MinElevationM { get; set; }
    public int UpdatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
