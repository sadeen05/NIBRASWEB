namespace NIBRAS.API.Services;

/// <summary>
/// Append-only security audit trail of the account lifecycle. The interface offers no way to
/// change or remove a recorded event, so the trail can only grow.
/// </summary>
public interface IUserSecurityEventService
{
    /// <summary>
    /// Records one security event for an account.
    /// </summary>
    /// <param name="userId">The account the event belongs to.</param>
    /// <param name="eventType">One of <c>NibrasWeb.Enums.UserSecurityEventTypes</c>.</param>
    /// <param name="actorId">The acting user, when somebody else caused the event.</param>
    /// <param name="ip">The remote address of the request, for abuse analysis.</param>
    /// <param name="details">
    /// Short, non-sensitive context. It must never carry a token, a password, a hash or any
    /// other secret; the value is stripped of control characters and truncated.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordAsync(
        int userId,
        string eventType,
        int? actorId = null,
        string? ip = null,
        string? details = null,
        CancellationToken cancellationToken = default);
}
