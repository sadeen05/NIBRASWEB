namespace NIBRAS.API.DTOs;

/// <summary>
/// Jordanian governorate lookup entry. <see cref="Code"/> is the stable
/// ISO 3166-2:JO suffix (e.g. "AM" for Amman).
/// </summary>
public class GovernorateDto
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public string NameEn { get; set; } = "";

    public string NameAr { get; set; } = "";
}
