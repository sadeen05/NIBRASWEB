using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public class CreateLandCriterionRequest
{
    // Upper bound matches the LAND_CRITERIA.MIN_AREA_DONUM column, decimal(10,2) (SEC-14 / 4.10).
    [Range(typeof(decimal), "0", "99999999.99")]
    public decimal? MinAreaDonum { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? MaxSlopePct { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal? MaxGridDistanceKm { get; set; }

    [Range(typeof(decimal), "0", "12")]
    public decimal? MinSolarIrradiance { get; set; }

    [Range(typeof(decimal), "-1000", "10000")]
    public decimal? MinElevationM { get; set; }

    [Required, NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string Reason { get; set; } = "";
}

public record LandCriterionDto(
    int Id,
    decimal? MinAreaDonum,
    decimal? MaxSlopePct,
    decimal? MaxGridDistanceKm,
    decimal? MinSolarIrradiance,
    decimal? MinElevationM,
    int UpdatedById,
    string UpdatedByName,
    DateTime? UpdatedAt,
    string Reason);
