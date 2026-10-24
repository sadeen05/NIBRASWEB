namespace NIBRAS.API.DTOs;

/// <summary>
/// One append-only technical assessment snapshot. Each row holds the effective
/// values after that assessment committed; a null measurement means the value was
/// not assessed and is never reported as zero.
/// </summary>
public record LandAssessmentDto(
    int Id,
    int LandId,
    int AssessedById,
    string AssessedByName,
    DateTime AssessedAt,
    string? Note,
    int? RegionId,
    int? GridId,
    decimal? SlopePercentage,
    decimal? DistanceToGridKm,
    decimal? SolarIrradiance,
    decimal? ElevationM,
    int? CriterionId = null,
    decimal? AreaDonumSnapshot = null,
    CriterionValuesDto? CriteriaSnapshot = null);
