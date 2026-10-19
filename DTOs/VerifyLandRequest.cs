using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

/// <summary>
/// Optional administrator approval note for <c>POST /api/lands/{id}/verify</c>.
/// <para>
/// The body is optional on purpose: every existing caller (the web client and the step
/// suites) sends no body at all, and the endpoint must keep accepting that. When a note
/// is supplied it is appended to the append-only land-status history reason, which is the
/// only place a verification decision is recorded — no dedicated column is added and the
/// schema is unchanged.
/// </para>
/// <para>
/// <c>MaxLength(400)</c> rather than 500: the note shares the
/// <c>LAND_STATUS_HISTORY.REASON</c> column (nvarchar(500)) with the fixed prefix
/// "Verified by admin against criteria version {id}. Note: ", whose longest possible form
/// is 61 characters (43 + a 10-digit criterion id + "." + " Note: "), so 400 + 61 = 461
/// stays inside the column. A 500-character note could overflow the column and surface as
/// a generic 500, which is why this bound is stricter than
/// <see cref="RejectLandRequest.Reason"/> (which occupies the column alone).
/// </para>
/// </summary>
public class VerifyLandRequest
{
    [NoControlCharactersExceptTabAndNewline, MaxLength(400)]
    public string? Note { get; set; }
}
