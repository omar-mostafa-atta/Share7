using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Feed;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Social;
using Share7.Domain.Feed;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Feed;

public class PlayerEventPublisher : IPlayerEventPublisher
{
    internal static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerOptions _options;

    public PlayerEventPublisher(ApplicationDbContext dbContext, IOptions<MultiplayerOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public void Stage(Guid recipientUserId, string type, object payload, DateTime? expiresAtUtc = null)
    {
        var now = DateTime.UtcNow;

        _dbContext.PlayerEvents.Add(new PlayerEvent
        {
            EventId = Guid.NewGuid(),
            RecipientUserId = recipientUserId,
            Type = type,
            Version = 1,
            PayloadJson = JsonSerializer.Serialize(payload, PayloadJson),
            OccurredAtUtc = now,
            ExpiresAtUtc = expiresAtUtc ?? now.AddDays(_options.EventRetentionDays)
        });
    }
}

/// <summary>
/// The long-poll. Answers at once when there are events after the cursor; otherwise waits — woken
/// in-process by <see cref="PlayerEventSignal"/>, and re-checking the database every
/// <c>EventFallbackPollSeconds</c> for events another instance committed — until one arrives or the
/// wait runs out.
/// <para>
/// Every read is also the player's presence heartbeat: a client polling its feed is a client that is
/// open.
/// </para>
/// </summary>
public class PlayerEventFeed : IPlayerEventFeed
{
    private const int PageSize = 100;

    private readonly ApplicationDbContext _dbContext;
    private readonly PlayerEventSignal _signal;
    private readonly IPresenceReader _presence;
    private readonly MultiplayerOptions _options;

    public PlayerEventFeed(
        ApplicationDbContext dbContext,
        PlayerEventSignal signal,
        IPresenceReader presence,
        IOptions<MultiplayerOptions> options)
    {
        _dbContext = dbContext;
        _signal = signal;
        _presence = presence;
        _options = options.Value;
    }

    public async Task<ServiceResult<PlayerEventPageDto>> ReadAsync(
        Guid userId,
        long after,
        int waitSeconds,
        CancellationToken cancellationToken = default)
    {
        if (after < 0)
            return ServiceResult<PlayerEventPageDto>.Failure(
                ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "after must be 0 or a sequence you were sent.");

        await _presence.TouchAsync(userId, cancellationToken);

        // The cursor names an event this player was sent. If that row is gone, retention deleted it —
        // and may have deleted events after it too — so the client re-reads state instead of
        // trusting a feed with a hole in it. A cursor that was never this player's reads the same way.
        if (after > 0 && !await _dbContext.PlayerEvents.AnyAsync(
                e => e.Sequence == after && e.RecipientUserId == userId, cancellationToken))
        {
            var latest = await _dbContext.PlayerEvents
                .Where(e => e.RecipientUserId == userId)
                .MaxAsync(e => (long?)e.Sequence, cancellationToken) ?? 0;

            return ServiceResult<PlayerEventPageDto>.Failure(
                ApiErrors.EventsCursorExpired,
                ServiceErrorKind.Gone,
                $"Cursor {after} is older than the {_options.EventRetentionDays}-day retention.",
                new Dictionary<string, object?> { ["latest"] = latest });
        }

        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(waitSeconds, 0, _options.EventMaxWaitSeconds));
        var fallback = TimeSpan.FromSeconds(Math.Max(0.05, _options.EventFallbackPollSeconds));

        while (true)
        {
            // Armed before the read, so an event that commits between the read and the wait still wakes it.
            var woken = _signal.Arm(userId);

            var page = await PageAsync(userId, after, cancellationToken);
            var remaining = deadline - DateTime.UtcNow;

            if (page.Count > 0 || remaining <= TimeSpan.Zero)
                return ServiceResult<PlayerEventPageDto>.Success(new PlayerEventPageDto
                {
                    Events = page,
                    NextAfter = page.Count > 0 ? page[^1].Sequence : after,
                    ServerTimeUtc = DateTime.UtcNow
                });

            try
            {
                await woken.WaitAsync(remaining < fallback ? remaining : fallback, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Nothing woke it: look again anyway, for an event another instance committed.
            }
        }
    }

    private async Task<List<PlayerEventDto>> PageAsync(Guid userId, long after, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var rows = await _dbContext.PlayerEvents
            .AsNoTracking()
            .Where(e => e.RecipientUserId == userId && e.Sequence > after && e.ExpiresAtUtc > now)
            .OrderBy(e => e.Sequence)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        return rows.Select(e => new PlayerEventDto
        {
            Sequence = e.Sequence,
            EventId = e.EventId,
            Type = e.Type,
            Version = e.Version,
            OccurredAtUtc = DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc),
            ExpiresAtUtc = DateTime.SpecifyKind(e.ExpiresAtUtc, DateTimeKind.Utc),
            Payload = ParsePayload(e.PayloadJson)
        }).ToList();
    }

    private static JsonElement ParsePayload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
