using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

/// <summary>
/// Admin-only technical assessment of a land. Every field is optional and a null
/// means "leave the stored value unchanged" — Phase 1 has no explicit clearing, so
/// a request can never erase an existing measurement. The DTO deliberately carries
/// no status, ownership, deletion, governorate or verification field, so an
/// assessment can never change any of them (SEC-07).
/// </summary>
public class AssessLandRequest
{
    [Range(1, int.MaxValue)]
    public int? RegionId { get; set; }

    [Range(1, int.MaxValue)]
    public int? GridId { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal? SlopePercentage { get; set; }

    [Range(typeof(decimal), "0", "1000000")]
    public decimal? DistanceToGridKm { get; set; }

    [Range(typeof(decimal), "0", "12")]
    public decimal? SolarIrradiance { get; set; }

    [Range(typeof(decimal), "-1000", "10000")]
    public decimal? ElevationM { get; set; }

    [NoControlCharactersExceptTabAndNewline, MaxLength(500)]
    public string? Note { get; set; }
}
