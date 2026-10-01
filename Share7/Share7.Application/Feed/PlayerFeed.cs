using System.Text.Json;
using Share7.Application.Common.Models;

namespace Share7.Application.Feed;

/// <summary>
/// Puts an event on a player's feed **as part of the caller's own unit of work**.
/// <para>
/// Staging adds the row to the caller's context; the caller's next <c>SaveChanges</c> writes it, in
/// the caller's transaction. That is what makes the feed an outbox rather than a second system that
/// can disagree with the first: the invite and its "you have an invite" commit or roll back together.
/// </para>
/// </summary>
public interface IPlayerEventPublisher
{
    /// <param name="payload">Serialised as camelCase JSON. Never a user's real name.</param>
    /// <param name="expiresAtUtc">When it stops being worth delivering; defaults to the retention period.</param>
    void Stage(Guid recipientUserId, string type, object payload, DateTime? expiresAtUtc = null);
}

/// <summary>The long-poll read of a player's own feed.</summary>
public interface IPlayerEventFeed
{
    /// <summary>
    /// Events for <paramref name="userId"/> after <paramref name="after"/>, in order. With nothing to
    /// return, waits up to <paramref name="waitSeconds"/> for something to arrive before answering
    /// empty. Refuses <c>EVENTS_CURSOR_EXPIRED</c> when the cursor is older than retention.
    /// </summary>
    Task<ServiceResult<PlayerEventPageDto>> ReadAsync(
        Guid userId,
        long after,
        int waitSeconds,
        CancellationToken cancellationToken = default);
}

public class PlayerEventDto
{
    /// <summary>The cursor. Send the last one you processed as <c>after</c>.</summary>
    public long Sequence { get; set; }

    /// <summary>De-duplicate on this: delivery is at least once.</summary>
    public Guid EventId { get; set; }

    public string Type { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public JsonElement Payload { get; set; }
}

public class PlayerEventPageDto
{
    public List<PlayerEventDto> Events { get; set; } = [];

    /// <summary>What to send as <c>after</c> next: the last event's sequence, or the cursor you sent.</summary>
    public long NextAfter { get; set; }

    public DateTime ServerTimeUtc { get; set; }
}
