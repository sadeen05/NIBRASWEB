using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

/// <summary>
/// Landlord land registration. The region, the grid and the four technical
/// measurements are deliberately absent: they are recorded later by an admin
/// technical assessment (<c>PUT /api/lands/{id}/assessment</c>) and can never be
/// supplied by a landlord.
/// </summary>
public class CreateLandRequest
{
    [Range(1, int.MaxValue)]
    public int GovernorateId { get; set; }

    [Required, NoControlCharacters, MaxLength(50)]
    public string LandNumber { get; set; } = "";

    [Required, NoControlCharacters, MaxLength(100)]
    public string Village { get; set; } = "";

    [Required, NoControlCharacters, MaxLength(100)]
    public string Basin { get; set; } = "";

    [Required, NoControlCharacters, MaxLength(100)]
    public string ParcelNumber { get; set; } = "";

    // Upper bound matches the LANDS.AREA_DONUM column, decimal(10,2) (SEC-14 / 4.10:
    // numbers have Range matching the DB column). A larger bound would pass validation
    // and then fail at the database with an arithmetic overflow (generic 500).
    [Range(typeof(decimal), "0.0001", "99999999.99")]
    public decimal AreaDonum { get; set; }

    // Optional free text; MaxLength matches the LANDS.DESCRIPTION column (SEC-14).
    [NoControlCharactersExceptTabAndNewline, MaxLength(1000)]
    public string? Description { get; set; }
}
