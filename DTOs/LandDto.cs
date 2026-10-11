namespace NIBRAS.API.DTOs;

public class LandDto
{
    public int Id { get; set; }
    public int LandlordId { get; set; }
    public int RegionId { get; set; }
    public string RegionName { get; set; } = "";
    public int GridId { get; set; }
    public string LandNumber { get; set; } = "";
    public string? Village { get; set; }
    public string? Basin { get; set; }
    public string? ParcelNumber { get; set; }
    public decimal AreaDonum { get; set; }
    public decimal SlopePercentage { get; set; }
    public decimal DistanceToGridKm { get; set; }
    public decimal SolarIrradiance { get; set; }
    public decimal ElevationM { get; set; }
    public string Status { get; set; } = "";
    public bool DataVerifiedByAdmin { get; set; }
    public int? VerifiedAgainstCriterionId { get; set; }
}

public record LandListResponse(
    IReadOnlyList<LandDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages);

public record LandCriterionResult(
    string Criterion,
    decimal? Required,
    decimal Actual,
    string Result);

public record LandEligibilityResponse(
    int LandId,
    int? CriterionId,
    IReadOnlyList<LandCriterionResult> Results);
