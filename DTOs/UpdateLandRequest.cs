using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

/// <summary>
/// Landlord land edit. As with <see cref="CreateLandRequest"/>, the region, the
/// grid and the technical measurements are absent: they belong to the admin
/// technical assessment and an owner edit can never overwrite or erase them.
/// </summary>
public class UpdateLandRequest
{
    /// <summary>
    /// Optional so that a land registered before governorates existed can still be
    /// edited without retroactively assigning one. When supplied it is validated and
    /// the duplicate keys move with it; when omitted the stored value is preserved.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? GovernorateId { get; set; }

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

    // Optional free text; MaxLength matches the LANDS.DESCRIPTION column (SEC-14).
    [NoControlCharactersExceptTabAndNewline, MaxLength(1000)]
    public string? Description { get; set; }
}
