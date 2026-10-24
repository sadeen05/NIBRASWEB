using System.Text;
using Microsoft.EntityFrameworkCore;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

/// <summary>
/// Writes the append-only security audit trail. Every event is inserted and never updated or
/// deleted, the event type must come from the closed set, and the free-text context is
/// stripped of control characters and truncated so it can never carry a runaway value.
/// </summary>
public sealed class UserSecurityEventService : IUserSecurityEventService
{
    private const int MaxDetailsLength = 500;
    private const int MaxIpLength = 45;

    private readonly NebrasdbContext _context;
    private readonly TimeProvider _clock;

    public UserSecurityEventService(NebrasdbContext context, TimeProvider? clock = null)
    {
        _context=context;
        _clock=clock ?? TimeProvider.System;
    }

    public async Task RecordAsync(
        int userId,
        string eventType,
        int? actorId = null,
        string? ip = null,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId), "The account id must be positive.");
        }

        if (!UserSecurityEventTypes.IsKnown(eventType))
        {
            throw new ArgumentException("Unknown security event type.", nameof(eventType));
        }

        _context.UserSecurityEvents.Add(new UserSecurityEvent
        {
            UserId = userId,
            EventType = eventType,
            ActorId = actorId,
            Ip = Truncate(ip, MaxIpLength),
            Details = Sanitize(details, MaxDetailsLength),
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>
    /// Removes control characters (tab and line feed are kept, as in the free-text fields of
    /// the API) and truncates the value to the column length.
    /// </summary>
    private static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character >= ' ' || character is '\t' or '\n')
            {
                builder.Append(character);
            }
        }

        var cleaned = builder.ToString();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}
