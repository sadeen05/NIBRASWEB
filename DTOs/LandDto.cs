namespace NIBRAS.API.DTOs;

public class LandDto
{
    public int Id { get; set; }
    public int LandlordId { get; set; }

    /// <summary>Assigned by an admin technical assessment; null until assessed.</summary>
    public int? RegionId { get; set; }

    /// <summary>Null until a region has been assigned by an assessment.</summary>
    public string? RegionName { get; set; }

    /// <summary>Assigned by an admin technical assessment; null until assessed.</summary>
    public int? GridId { get; set; }

    /// <summary>Governorate chosen by the landlord; null only for pre-governorate records.</summary>
    public int? GovernorateId { get; set; }

    public string? GovernorateName { get; set; }

    public string LandNumber { get; set; } = "";
    public string? Village { get; set; }
    public string? Basin { get; set; }
    public string? ParcelNumber { get; set; }
    public string? Description { get; set; }
    public decimal AreaDonum { get; set; }

    /// <summary>Null means "not yet assessed"; never reported as zero.</summary>
    public decimal? SlopePercentage { get; set; }

    /// <summary>Null means "not yet assessed"; never reported as zero.</summary>
    public decimal? DistanceToGridKm { get; set; }

    /// <summary>Null means "not yet assessed"; never reported as zero.</summary>
    public decimal? SolarIrradiance { get; set; }

    /// <summary>Null means "not yet assessed"; never reported as zero.</summary>
    public decimal? ElevationM { get; set; }

    public string Status { get; set; } = "";
    public bool DataVerifiedByAdmin { get; set; }
    public int? VerifiedAgainstCriterionId { get; set; }

    /// <summary>
    /// True while an open change request hides this approved land from investors.
    /// The status stays <c>Verified</c> during the review.
    /// </summary>
    public bool IsHiddenFromInvestors { get; set; }
}

public record LandListResponse(
    IReadOnlyList<LandDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>
/// One criterion outcome. <c>Result</c> is one of <c>NotEnforced</c> (no threshold
/// configured), <c>NotAssessed</c> (threshold enforced but the land has no recorded
/// measurement — never a pass), <c>Passed</c> or <c>Failed</c>. <c>Actual</c> is null
/// when the measurement has not been recorded.
/// </summary>
public record LandCriterionResult(
    string Criterion,
    decimal? Required,
    decimal? Actual,
    string Result);

public record LandEligibilityResponse(
    int LandId,
    int? CriterionId,
    IReadOnlyList<LandCriterionResult> Results);
