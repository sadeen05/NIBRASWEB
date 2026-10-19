using System;

namespace NIBRAS.Models;

/// <summary>
/// A single-use email-ownership token. Only the SHA-256 hash of the token is stored; the
/// plaintext exists solely in the activation link that was mailed to the address, and it is
/// never written to a log or to a database column.
/// </summary>
public partial class EmailVerificationToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>One of <see cref="NibrasWeb.Enums.EmailVerificationPurposes"/>.</summary>
    public string Purpose { get; set; } = null!;

    /// <summary>
    /// SHA-256 of the token (32 bytes). The hash is the lookup key, so it is unique: a token
    /// row can only ever be found through its hash, never through a plaintext token.
    /// </summary>
    public byte[] TokenHash { get; set; } = null!;

    /// <summary>
    /// The address that was active when a change was requested. Kept for the audit trail only:
    /// an active address is replaced solely by a successful verification.
    /// </summary>
    public string? PreviousEmail { get; set; }

    /// <summary>When the token stops being usable (24h for a registration, 1h for a change).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the token is consumed. A token can be consumed exactly once.</summary>
    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>The address the request came from, for abuse analysis. Never a credential.</summary>
    public string? RequestIp { get; set; }

    public virtual User User { get; set; } = null!;
}
