using Microsoft.EntityFrameworkCore;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Decides whether a result is believable enough to rank.
/// <para>
/// **Flag, never reject.** Every rule here fires on honest players too — a bad device clock, a
/// dropped connection, a classroom of children all finishing at once — so the answer to a
/// suspicious result is to keep it, leave it out of the ranking, and let a person look. A
/// leaderboard that silently deletes a child's genuine run has done more damage than the cheat it
/// was guarding against.
/// </para>
/// <para>
/// This is the compensating control for the answer key being client-visible. The client grades
/// locally so that a quiz can show right or wrong the instant a child taps, which means a modified
/// build can always submit a perfect run — so the defence cannot be secrecy, and has to be "that
/// many perfect runs that fast is not a person".
/// </para>
/// </summary>
public class PlausibilityGuard : IPlausibilityGuard
{
    private readonly ApplicationDbContext _dbContext;

    public PlausibilityGuard(ApplicationDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// Returns the reason a result should be flagged, or null when it looks fine.
    /// <para>
    /// Reads the day's history once per (user, metric) rather than per bound, because this runs on
    /// the gameplay request and a leaderboard must never add a query per rule to finishing a
    /// lesson.
    /// </para>
    /// </summary>
    public async Task<string?> ReasonToFlagAsync(
        Guid userId,
        Guid gameId,
        Guid? modeId,
        string metric,
        long value,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _dbContext.LeaderboardMetricBounds
            .AsNoTracking()
            .Where(b => b.Enabled
                        && b.Metric == metric
                        && (b.GameId == null || b.GameId == gameId)
                        && (b.ModeId == null || b.ModeId == modeId))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return null;

        // **The most specific bound wins outright rather than composing with the looser ones.** A
        // mode exists precisely because its numbers are different, so a per-mode row is a statement
        // that the game-wide ceiling does not apply here — and applying both would mean the tighter
        // general bound still flagged every honest endless run.
        var bounds = candidates.Any(b => b.ModeId is not null)
            ? candidates.Where(b => b.ModeId is not null).ToList()
            : candidates;

        // A future timestamp is not a bound violation, it is a broken clock — and it is worth
        // catching separately because it would otherwise land the result in a cycle that has not
        // opened. Generous tolerance: a few minutes of drift is ordinary on a cheap tablet.
        if (occurredAtUtc > DateTime.UtcNow.AddMinutes(5))
            return "Result timestamp is in the future.";

        foreach (var bound in bounds)
        {
            if (bound.MaxValue is { } ceiling && value > ceiling)
                return $"Value {value} exceeds the {metric} ceiling of {ceiling}.";
        }

        var needsHistory = bounds.Any(b => b.MaxResultsPerDay is not null || b.MaxValuePerDay is not null);

        if (!needsHistory)
            return null;

        var startOfDayUtc = occurredAtUtc.Date;

        // Counted rather than reserved, exactly as the reward engine's daily limit is: two results
        // racing at the boundary can both pass and take the total one over. Tolerated on purpose —
        // the alternative is a counter row to lock on the gameplay path, which is a great deal of
        // machinery to stop somebody recording one extra lesson.
        var today = await _dbContext.GameResults
            .AsNoTracking()
            .Where(r => r.UserId == userId
                        && r.Metric == metric
                        && r.OccurredAtUtc >= startOfDayUtc
                        && r.OccurredAtUtc < startOfDayUtc.AddDays(1))
            .GroupBy(r => 1)
            .Select(group => new { Count = group.Count(), Total = group.Sum(r => r.Value) })
            .FirstOrDefaultAsync(cancellationToken);

        var countToday = today?.Count ?? 0;
        var totalToday = today?.Total ?? 0;

        foreach (var bound in bounds)
        {
            if (bound.MaxResultsPerDay is { } maxCount && countToday + 1 > maxCount)
                return $"More than {maxCount} {metric} results in one day.";

            if (bound.MaxValuePerDay is { } maxTotal && totalToday + value > maxTotal)
                return $"More than {maxTotal} total {metric} in one day.";
        }

        return null;
    }
}
