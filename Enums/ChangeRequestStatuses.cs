namespace NibrasWeb.Enums;

/// <summary>
/// Lifecycle of a landlord change request on an already-approved land. Kept as
/// strings (like <see cref="DocumentStatuses"/>) so the value is stored verbatim in
/// the single <c>LAND_CHANGE_REQUESTS.STATUS</c> column.
/// </summary>
public static class ChangeRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}
