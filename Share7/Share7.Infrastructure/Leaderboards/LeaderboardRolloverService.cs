using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Advances cycle states and makes sure the next window exists before anyone needs it.
/// <para>
/// **Cycles are rows created ahead of time, never a window computed at read time.** A derived
/// window has no identity, so nothing can be settled against it, rewarded from it, cached by it or
/// linked to it — and two servers a millisecond apart would disagree about which window a result
/// belongs in. Creating the row makes the answer a fact.
/// </para>
/// </summary>
public class LeaderboardRolloverService : ILeaderboardRolloverService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<LeaderboardRolloverService> _logger;

    public LeaderboardRolloverService(
        ApplicationDbContext dbContext, ILogger<LeaderboardRolloverService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> RolloverAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var changed = 0;

        var boards = await _dbContext.LeaderboardBoards
            .Where(b => b.IsActive)
            .Include(b => b.Cycles.Where(c => c.State != LeaderboardCycleState.Settled))
            .ToListAsync(cancellationToken);

        foreach (var board in boards)
        {
            // Scheduled -> Open. Done before closing so a board is never briefly without a live
            // window at the exact moment one ends and the next begins.
            foreach (var due in board.Cycles.Where(c =>
                         c.State == LeaderboardCycleState.Scheduled && c.StartsAtUtc <= now))
            {
                due.State = LeaderboardCycleState.Open;
                changed++;
            }

            // Open -> Closed. Ranks freeze here rather than at settlement, so the settlement job is
            // never racing the last second of play.
            foreach (var expired in board.Cycles.Where(c =>
                         c.State == LeaderboardCycleState.Open && c.EndsAtUtc <= now))
            {
                expired.State = LeaderboardCycleState.Closed;
                expired.ClosedAtUtc = now;
                changed++;

                // Settlement runs after the grace window, not immediately, so a result still in
                // flight from a child on a bad connection is counted before the prizes are cut.
                Enqueue(LeaderboardJobKind.Settle, expired.Id, board.Id,
                    now.AddSeconds(board.GraceSeconds + 30));
            }

            if (await EnsureLiveWindowAsync(board, now, cancellationToken))
                changed++;
        }

        if (changed > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        return changed;
    }

    /// <summary>
    /// Creates the window covering now, if the board has none.
    /// <para>
    /// Relies on the unique index over <c>(BoardId, StartsAtUtc)</c> rather than on being the only
    /// caller: two workers rolling over at once is normal on a host that can start a second
    /// process at any time, and the loser must collide instead of creating a duplicate window that
    /// would split a week's ranking in half.
    /// </para>
    /// </summary>
    private async Task<bool> EnsureLiveWindowAsync(
        LeaderboardBoard board, DateTime now, CancellationToken cancellationToken)
    {
        if (board.Period == LeaderboardPeriod.Event)
            return false;

        var window = LeaderboardCycleFactory.WindowFor(board.Period, now);

        if (window is not { } bounds)
            return false;

        var exists = board.Cycles.Any(c => c.StartsAtUtc == bounds.StartsAtUtc)
                     || await _dbContext.LeaderboardCycles.AnyAsync(
                         c => c.BoardId == board.Id && c.StartsAtUtc == bounds.StartsAtUtc,
                         cancellationToken);

        if (exists)
            return false;

        _dbContext.LeaderboardCycles.Add(new LeaderboardCycle
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StartsAtUtc = bounds.StartsAtUtc,
            EndsAtUtc = bounds.EndsAtUtc,
            State = LeaderboardCycleState.Open,
            CreatedAtUtc = now
        });

        _logger.LogInformation(
            "Opened a new {Period} cycle for board {BoardKey}.", board.Period, board.BoardKey);

        return true;
    }

    private void Enqueue(LeaderboardJobKind kind, Guid cycleId, Guid boardId, DateTime runAfterUtc) =>
        _dbContext.LeaderboardJobs.Add(new LeaderboardJob
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            CycleId = cycleId,
            BoardId = boardId,
            State = LeaderboardJobState.Pending,
            RunAfterUtc = runAfterUtc,
            CreatedAtUtc = DateTime.UtcNow
        });
}
