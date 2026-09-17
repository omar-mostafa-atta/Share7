using System.Text.Json;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Entity to DTO, in one place so every endpoint returns an identically-shaped session.
/// </summary>
internal static class MultiplayerMappings
{
    /// <summary>
    /// camelCase, matching the wire. The curriculum path is stored as the same JSON the client sent
    /// and the client reads back, so serialising it with different options here would quietly change
    /// a payload that is supposed to be echoed verbatim.
    /// </summary>
    internal static readonly JsonSerializerOptions CurriculumPathJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string? SerializePath(CurriculumPathDto? path) =>
        path is null ? null : JsonSerializer.Serialize(path, CurriculumPathJson);

    /// <summary>
    /// Reads the stored path back.
    /// <para>
    /// **Never throws.** The column is opaque to the backend by design, so a blob written by a newer
    /// client — or hand-edited during support — must degrade to "no path" rather than making the
    /// session unreadable. A session the client cannot fetch is worse than one missing a filter hint.
    /// </para>
    /// </summary>
    public static CurriculumPathDto? DeserializePath(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<CurriculumPathDto>(json, CurriculumPathJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Re-stamps a timestamp read back from the database as UTC.
    /// <para>
    /// **Every timestamp on the wire must go through this.** SQL Server's <c>datetime2</c> carries no
    /// timezone, so EF materialises it as <c>DateTimeKind.Unspecified</c> and the serializer has
    /// nothing to mark it with — the value goes out as <c>2026-08-18T22:22:31.6081797</c> while
    /// <c>serverTimeUtc</c>, generated in memory, goes out as <c>…905941Z</c>. A naive
    /// <c>DateTime.Parse</c> reads the unmarked one as **local time** and silently shifts it by the
    /// device's offset.
    /// </para>
    /// <para>
    /// That is a documented trap in this API (<c>ResponseSchemas.md</c> §2, where the progress
    /// timestamps still have it) and it bites hardest here: the client computes heartbeat drift by
    /// comparing these against <c>serverTimeUtc</c>, so in Cairo the two would disagree by three
    /// hours and every session would look catastrophically out of sync. The equipment read path
    /// already solved it the same way.
    /// </para>
    /// </summary>
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value is { } set ? AsUtc(set) : null;

    public static MultiplayerSessionPlayerDto ToDto(this MultiplayerSessionPlayer player, string? displayName) =>
        new()
        {
            UserId = player.UserId,
            DisplayName = displayName,
            Slot = player.Slot,
            IsHost = player.IsHost,
            Status = player.Status,
            JoinedAtUtc = AsUtc(player.JoinedAtUtc),
            LastSeenAtUtc = AsUtc(player.LastSeenAtUtc)
        };

    /// <summary>
    /// <paramref name="displayNames"/> is looked up once per response by the caller rather than per
    /// player here — a roster of four would otherwise be four queries.
    /// </summary>
    public static MultiplayerSessionDto ToDto(
        this MultiplayerSession session,
        IReadOnlyDictionary<Guid, string> displayNames,
        DateTime serverTimeUtc) =>
        new()
        {
            Id = session.Id,
            GameId = session.GameId,
            HostUserId = session.HostUserId,
            TransportSessionName = session.TransportSessionName,
            TransportRegion = session.TransportRegion,
            JoinCode = session.JoinCode,
            State = session.State,
            Visibility = session.Visibility,
            MaxPlayers = session.MaxPlayers,
            MinPlayers = session.MinPlayers,
            CurrentPlayerCount = session.CurrentPlayerCount,
            ProtocolVersion = session.ProtocolVersion,
            IsRanked = session.IsRanked,
            CurriculumPath = DeserializePath(session.CurriculumPathJson),
            CreatedAtUtc = AsUtc(session.CreatedAtUtc),
            StartedAtUtc = AsUtc(session.StartedAtUtc),
            EndedAtUtc = AsUtc(session.EndedAtUtc),
            ServerTimeUtc = AsUtc(serverTimeUtc),

            // Seated members only, in seat order. A departed membership is history — the client
            // renders seats, and a roster that included them would show ghosts.
            Players = session.Players
                .Where(p => !p.Status.HasDeparted())
                .OrderBy(p => p.Slot)
                .Select(p => p.ToDto(displayNames.GetValueOrDefault(p.UserId)))
                .ToList()
        };
}
