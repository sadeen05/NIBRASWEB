using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

/// <summary>
/// Optional body for <c>POST /api/grid-capacity-reservations/{id}/release</c>.
/// An empty body is accepted and a default reason is recorded.
/// </summary>
public class ReleaseGridCapacityReservationRequest
{
    [NoControlCharactersExceptTabAndNewline, MaxLength(300)]
    public string? Reason { get; set; }
}
