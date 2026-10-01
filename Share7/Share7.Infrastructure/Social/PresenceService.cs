using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Social;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Social;

/// <summary>
/// Presence from two sources that already exist: seats (in a lobby, in a match — authoritative) and
/// the feed poll (the app is open). No heartbeat of its own, no socket, no cache server.
/// </summary>
public class PresenceService : IPresenceReader
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly MultiplayerOptions _options;

    public PresenceService(ApplicationDbContext dbContext, IMemoryCache cache, IOptions<MultiplayerOptions> options)
    {
        _dbContext = dbContext;
        _cache = cache;
        _options = options.Value;
    }

    public async Task TouchAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // A client long-polls every few seconds; the database hears about it at most every
        // PresenceWriteSeconds. Per instance, which is fine — the window is what matters, not the count.
        var key = $"presence:{userId:N}";
        if (_cache.TryGetValue(key, out _))
            return;

        var now = DateTime.UtcNow;

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE [PlayerPresence] SET [LastSeenAtUtc] = {1} WHERE [UserId] = {0};
                IF @@ROWCOUNT = 0
                    INSERT INTO [PlayerPresence] ([UserId], [LastSeenAtUtc]) VALUES ({0}, {1});
                """,
                [userId, now],
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            // Two first touches raced to insert; the other one's row says the same thing.
        }

        _cache.Set(key, true, TimeSpan.FromSeconds(Math.Max(1, _options.PresenceWriteSeconds)));
    }

    public async Task<IReadOnlyDictionary<Guid, PlayerPresenceState>> GetAsync(
        IReadOnlyCollection<Guid> users, CancellationToken cancellationToken = default)
    {
        if (users.Count == 0)
            return new Dictionary<Guid, PlayerPresenceState>();

        var ids = users.Distinct().ToList();

        var seats = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId)
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .Select(p => new { p.UserId, p.Session!.State })
            .ToListAsync(cancellationToken);

        var onlineSince = DateTime.UtcNow.AddSeconds(-_options.PresenceOnlineSeconds);

        var online = await _dbContext.PlayerPresence
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId) && p.LastSeenAtUtc >= onlineSince)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

        var result = ids.ToDictionary(id => id, _ => PlayerPresenceState.Offline);

        foreach (var id in online)
            result[id] = PlayerPresenceState.Online;

        foreach (var seat in seats)
        {
            result[seat.UserId] = seat.State switch
            {
                MultiplayerSessionState.Creating or MultiplayerSessionState.Created => PlayerPresenceState.InLobby,
                MultiplayerSessionState.Starting or MultiplayerSessionState.Running or MultiplayerSessionState.Ending
                    => PlayerPresenceState.InMatch,
                _ => result[seat.UserId]
            };
        }

        return result;
    }
}
