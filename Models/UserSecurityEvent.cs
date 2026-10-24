using System;

namespace NIBRAS.Models;

/// <summary>
/// Append-only security audit trail for account lifecycle events: registration, verification
/// mail delivery, verification outcome, blocked logins, address changes and admin onboarding.
/// Rows are never updated and never deleted, so an account's security history stays readable
/// even after the account itself is soft-deleted.
/// </summary>
public partial class UserSecurityEvent
{
    public int Id { get; set; }

    /// <summary>The account the event belongs to.</summary>
    public int UserId { get; set; }

    /// <summary>One of <see cref="NibrasWeb.Enums.UserSecurityEventTypes"/>.</summary>
    public string EventType { get; set; } = null!;

    /// <summary>
    /// The acting user when somebody else caused the event (for example a SuperAdmin creating
    /// an Admin account). NULL means the account holder or the system itself.
    /// </summary>
    public int? ActorId { get; set; }

    /// <summary>The address the request came from, for abuse analysis. Never a credential.</summary>
    public string? ActorNameSnapshot { get; set; }

    public string? ActorRoleSnapshot { get; set; }

    public string? Ip { get; set; }

    /// <summary>Free-form, non-sensitive context. Never a token, a password or a hash.</summary>
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual User? Actor { get; set; }
}
