using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// The operator surface. See <see cref="IMultiplayerAdminService"/> for why it is a separate service.
/// </summary>
public class MultiplayerAdminService : IMultiplayerAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerSessionService _sessions;

    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    public MultiplayerAdminService(ApplicationDbContext dbContext, MultiplayerSessionService sessions)
    {
        _dbContext = dbContext;
        _sessions = sessions;
    }

    public async Task<ServiceResult<MultiplayerAdminSessionsDto>> ListAsync(
        MultiplayerAdminQuery query,
        CancellationToken cancellationToken = default)
    {
        var sessions = _dbContext.MultiplayerSessions.AsNoTracking();

        if (query.GameId is { } gameId)
            sessions = sessions.Where(s => s.GameId == gameId);

        if (query.State is { } state)
            sessions = sessions.Where(s => s.State == state);

        if (query.OlderThanUtc is { } olderThan)
            sessions = sessions.Where(s => s.CreatedAtUtc < olderThan);

        // Counted before the limit so a truncated answer is visibly truncated. An operator who
        // cannot tell "these are all of them" from "these are the first hundred" will read the
        // second as the first, which at 3 AM is how the wrong conclusion gets drawn.
        var totalMatching = await sessions.CountAsync(cancellationToken);

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);

        var rows = await sessions
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return ServiceResult<MultiplayerAdminSessionsDto>.Success(new MultiplayerAdminSessionsDto
        {
            Sessions = rows.Select(ToSummary).ToList(),
            TotalMatching = totalMatching,
            ServerTimeUtc = DateTime.UtcNow
        });
    }

    public async Task<ServiceResult<IReadOnlyList<MultiplayerSessionPlayerDto>>> GetPlayersAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.MultiplayerSessions.AnyAsync(s => s.Id == sessionId, cancellationToken))
            return ServiceResult<IReadOnlyList<MultiplayerSessionPlayerDto>>.Failure(
                ApiErrors.SessionNotFound,
                ServiceErrorKind.NotFound,
                $"Session {sessionId} does not exist.");

        // **Departed members included**, unlike the player-facing roster: who left and when is the
        // substance of most support questions, and a closed session would otherwise read as empty.
        var players = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.Slot)
            .ToListAsync(cancellationToken);

        var names = await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => players.Select(x => x.UserId).Contains(p.UserId) && p.FullName != string.Empty)
            .ToDictionaryAsync(p => p.UserId, p => p.FullName, cancellationToken);

        return ServiceResult<IReadOnlyList<MultiplayerSessionPlayerDto>>.Success(
            players.Select(p => p.ToDto(names.GetValueOrDefault(p.UserId))).ToList());
    }

    public async Task<ServiceResult<MultiplayerSessionSummaryDto>> CloseAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.MultiplayerSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
            return ServiceResult<MultiplayerSessionSummaryDto>.Failure(
                ApiErrors.SessionNotFound,
                ServiceErrorKind.NotFound,
                $"Session {sessionId} does not exist.");

        // Routed through the session service's own close so the memberships are released by the same
        // code that releases them everywhere else. An admin close that forgot to do that would leave
        // every player in the session unable to join anything again.
        await _sessions.ApplyCloseAsync(session, SessionClosedReason.AdminClosed, cancellationToken);

        var closed = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == sessionId, cancellationToken);

        return ServiceResult<MultiplayerSessionSummaryDto>.Success(ToSummary(closed));
    }

    private static MultiplayerSessionSummaryDto ToSummary(MultiplayerSession session) => new()
    {
        Id = session.Id,
        GameId = session.GameId,
        HostUserId = session.HostUserId,
        TransportSessionName = session.TransportSessionName,
        TransportRegion = session.TransportRegion,
        JoinCode = session.JoinCode,
        State = session.State,
        Visibility = session.Visibility,
        MinPlayers = session.MinPlayers,
        MaxPlayers = session.MaxPlayers,
        CurrentPlayerCount = session.CurrentPlayerCount,
        ProtocolVersion = session.ProtocolVersion,
        IsRanked = session.IsRanked,
        LessonId = session.LessonId,

        // Re-stamped as UTC, like every other timestamp this API puts on the wire — see
        // MultiplayerMappings.AsUtc. Values read back from datetime2 arrive Unspecified and would
        // otherwise serialise without the Z.
        CreatedAtUtc = AsUtc(session.CreatedAtUtc),
        StartedAtUtc = AsUtc(session.StartedAtUtc),
        EndedAtUtc = AsUtc(session.EndedAtUtc),
        LastHeartbeatAtUtc = AsUtc(session.LastHeartbeatAtUtc),
        ClosedReason = session.ClosedReason
    };

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value is { } set ? AsUtc(set) : null;
}
