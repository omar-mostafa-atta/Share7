using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// The five cleanup rules. See <see cref="IMultiplayerSweepService"/> for why this is a service
/// rather than logic inside the background worker.
/// </summary>
public class MultiplayerSweepService : IMultiplayerSweepService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<MultiplayerSweepService> _logger;

    /// <summary>
    /// Rows touched per rule per pass.
    /// <para>
    /// A bound rather than "everything that matches" so a backlog — a bad deploy, a database that
    /// was down for an hour — cannot hold one enormous transaction open and block live traffic. The
    /// pass is idempotent, so a backlog simply drains over several passes.
    /// </para>
    /// </summary>
    private const int BatchSize = 200;

    public MultiplayerSweepService(
        ApplicationDbContext dbContext,
        IOptions<MultiplayerOptions> options,
        ILogger<MultiplayerSweepService> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MultiplayerSweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // **Order matters, though correctness does not depend on it.** Releasing disconnected
        // players before closing empty sessions means a session whose last member just timed out is
        // closed in the same pass rather than the next one. Creating-timeout runs before the
        // heartbeat sweep because a stuck Creating session also has a stale heartbeat, and
        // CreationFailed is the more specific — and more useful — reason to record.
        var failedCreating = await FailStuckCreatingAsync(now, cancellationToken);
        var abandoned = await AbandonSilentAsync(now, cancellationToken);
        var playersReleased = await ReleaseDisconnectedPlayersAsync(now, cancellationToken);
        var closedEmpty = await CloseEmptyAsync(now, cancellationToken);
        var logsPurged = await PurgeRequestLogsAsync(now, cancellationToken);

        var result = new MultiplayerSweepResult(
            failedCreating, abandoned, closedEmpty, playersReleased, logsPurged);

        if (result.Total > 0)
            _logger.LogInformation(
                "Multiplayer sweep: {FailedCreating} failed creating, {Abandoned} abandoned, "
                + "{ClosedEmpty} closed empty, {PlayersReleased} players released, {LogsPurged} logs purged.",
                failedCreating, abandoned, closedEmpty, playersReleased, logsPurged);

        return result;
    }

    /// <summary>
    /// A session that never confirmed its transport room. It holds a room name nobody can use and
    /// keeps its host locked out of starting anything else, so it is failed rather than left.
    /// </summary>
    private async Task<int> FailStuckCreatingAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddSeconds(-_options.CreatingTimeoutSeconds);

        var ids = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.State == MultiplayerSessionState.Creating && s.CreatedAtUtc < cutoff)
            .OrderBy(s => s.CreatedAtUtc)
            .Take(BatchSize)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return 0;

        // The state predicate is repeated in the UPDATE, not just in the SELECT above. A session that
        // was confirmed in the gap between the two must not be failed out from under a host who just
        // got their room up.
        var updated = await _dbContext.MultiplayerSessions
            .Where(s => ids.Contains(s.Id) && s.State == MultiplayerSessionState.Creating)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.State, MultiplayerSessionState.Failed)
                    .SetProperty(s => s.ClosedReason, SessionClosedReason.CreationFailed)
                    .SetProperty(s => s.EndedAtUtc, now)
                    .SetProperty(s => s.CurrentPlayerCount, 0),
                cancellationToken);

        await DepartMembershipsAsync(ids, now, cancellationToken);

        return updated;
    }

    /// <summary>
    /// The host stopped talking. **This is the rule that makes a crashed host survivable** — without
    /// it the session would hold its room name and its members' one active membership forever.
    /// </summary>
    private async Task<int> AbandonSilentAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddSeconds(-_options.SessionTimeoutSeconds);

        var ids = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.State != MultiplayerSessionState.Closed
                        && s.State != MultiplayerSessionState.Failed
                        && s.State != MultiplayerSessionState.Abandoned
                        && s.LastHeartbeatAtUtc < cutoff)
            .OrderBy(s => s.LastHeartbeatAtUtc)
            .Take(BatchSize)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return 0;

        var updated = await _dbContext.MultiplayerSessions
            .Where(s => ids.Contains(s.Id)
                        && s.State != MultiplayerSessionState.Closed
                        && s.State != MultiplayerSessionState.Failed
                        && s.State != MultiplayerSessionState.Abandoned
                        && s.LastHeartbeatAtUtc < cutoff)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.State, MultiplayerSessionState.Abandoned)
                    .SetProperty(s => s.ClosedReason, SessionClosedReason.Abandoned)
                    .SetProperty(s => s.EndedAtUtc, now)
                    .SetProperty(s => s.CurrentPlayerCount, 0),
                cancellationToken);

        await DepartMembershipsAsync(ids, now, cancellationToken);

        return updated;
    }

    /// <summary>
    /// A member who has been missing longer than the grace period gives up their seat. The heartbeat
    /// marks them Disconnected; only this promotes it to Left, so a host with a flaky connection
    /// cannot evict anybody by failing to see them for a moment.
    /// </summary>
    private async Task<int> ReleaseDisconnectedPlayersAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddSeconds(-_options.PlayerDisconnectGraceSeconds);

        var stale = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.Status == SessionPlayerStatus.Disconnected && p.LastSeenAtUtc < cutoff)
            .OrderBy(p => p.LastSeenAtUtc)
            .Take(BatchSize)
            .Select(p => new { p.Id, p.SessionId })
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
            return 0;

        var ids = stale.Select(p => p.Id).ToList();

        var released = await _dbContext.MultiplayerSessionPlayers
            .Where(p => ids.Contains(p.Id) && p.Status == SessionPlayerStatus.Disconnected)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Status, SessionPlayerStatus.Left)
                    .SetProperty(p => p.LeftAtUtc, now),
                cancellationToken);

        // Recount rather than decrement. A decrement assumes it knows how many rows it just changed
        // in each session, and drift here is what makes a session look full when it is empty — so the
        // count is recomputed from the memberships that are actually left.
        foreach (var sessionId in stale.Select(p => p.SessionId).Distinct())
        {
            var seated = await _dbContext.MultiplayerSessionPlayers
                .CountAsync(p => p.SessionId == sessionId
                                 && p.Status != SessionPlayerStatus.Left
                                 && p.Status != SessionPlayerStatus.Removed,
                    cancellationToken);

            await _dbContext.MultiplayerSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.CurrentPlayerCount, seated), cancellationToken);
        }

        return released;
    }

    /// <summary>An open session nobody is in. Closes it so its room name goes back into circulation.</summary>
    private async Task<int> CloseEmptyAsync(DateTime now, CancellationToken cancellationToken)
    {
        // Ordered for the same reason as every other rule here: TOP without ORDER BY takes an
        // arbitrary set, which under a sustained backlog can leave the same rows unvisited pass
        // after pass. Oldest first guarantees the queue drains.
        var ids = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.State == MultiplayerSessionState.Created && s.CurrentPlayerCount == 0)
            .OrderBy(s => s.CreatedAtUtc)
            .Take(BatchSize)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return 0;

        return await _dbContext.MultiplayerSessions
            .Where(s => ids.Contains(s.Id)
                        && s.State == MultiplayerSessionState.Created
                        && s.CurrentPlayerCount == 0)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.State, MultiplayerSessionState.Closed)
                    .SetProperty(s => s.ClosedReason, SessionClosedReason.Empty)
                    .SetProperty(s => s.EndedAtUtc, now),
                cancellationToken);
    }

    /// <summary>
    /// Expired idempotency keys. The window is far longer than any client retry budget, so a deleted
    /// row is one nobody could still be retrying against.
    /// </summary>
    private async Task<int> PurgeRequestLogsAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddHours(-_options.RequestLogRetentionHours);

        return await _dbContext.MultiplayerRequestLogs
            .Where(l => l.CreatedAtUtc < cutoff)
            .OrderBy(l => l.CreatedAtUtc)
            .Take(BatchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Marks every seat in these sessions released. A terminal session with members still seated
    /// would keep those accounts from joining anything else — the one-session-at-a-time rule reads
    /// membership, not session state.
    /// </summary>
    private Task DepartMembershipsAsync(
        IReadOnlyCollection<Guid> sessionIds,
        DateTime now,
        CancellationToken cancellationToken) =>
        _dbContext.MultiplayerSessionPlayers
            .Where(p => sessionIds.Contains(p.SessionId)
                        && p.Status != SessionPlayerStatus.Left
                        && p.Status != SessionPlayerStatus.Removed)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Status, SessionPlayerStatus.Left)
                    .SetProperty(p => p.LeftAtUtc, now),
                cancellationToken);
}
