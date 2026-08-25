using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Progression.Interfaces;
using Share7.Application.Progression.Models;
using Share7.Domain.Constants;
using Share7.Domain.Progression;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Progression;

/// <inheritdoc cref="ILevelService"/>
public class LevelService : ILevelService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILevelCurveCache _curve;

    public LevelService(ApplicationDbContext dbContext, ILevelCurveCache? curve = null)
    {
        _dbContext = dbContext;

        // Optional so a test can construct this with a context and nothing else. A private cache is
        // then per-instance, which is exactly the old behaviour and correct for a test.
        _curve = curve ?? new LevelCurveCache();
    }

    public Guid XpCurrencyId => CurrencyIds.Xp;

    public string XpCurrencyKey => CurrencyKeys.Xp;

    public async Task<PlayerLevelDto> DescribeAsync(long xp, CancellationToken cancellationToken = default)
    {
        if (xp < 0) xp = 0;

        var curve = await CurveAsync(cancellationToken);

        // No curve authored. Level 1 with an empty band is the honest answer: the player has not
        // been placed anywhere, and reporting a fabricated ladder would be worse than reporting
        // none. Deliberately not an exception — a missing curve must not fail a results screen.
        if (curve.Count == 0)
        {
            return new PlayerLevelDto { Level = 1, Xp = xp, IsMaxLevel = true };
        }

        var index = IndexFor(curve, xp);
        var current = curve[index];
        var isMax = index == curve.Count - 1;

        if (isMax)
        {
            return new PlayerLevelDto
            {
                Level = current.Level,
                Xp = xp,
                XpIntoLevel = xp - current.CumulativeXp,
                IsMaxLevel = true
            };
        }

        var next = curve[index + 1];

        return new PlayerLevelDto
        {
            Level = current.Level,
            Xp = xp,
            XpIntoLevel = xp - current.CumulativeXp,
            // The band's width, not the next threshold — that is what fills a progress bar without
            // the client doing a second subtraction against a number it would have to be told.
            XpForNextLevel = next.CumulativeXp - current.CumulativeXp,
            XpToNextLevel = next.CumulativeXp - xp,
            IsMaxLevel = false
        };
    }

    public async Task<PlayerLevelDto> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var xp = await XpBalanceAsync(userId, cancellationToken);

        return await DescribeAsync(xp, cancellationToken);
    }

    public async Task<IReadOnlyList<int>> LevelsCrossedAsync(
        long xpBefore,
        long xpAfter,
        CancellationToken cancellationToken = default)
    {
        // A correction that removes XP does not un-level anybody: the ladder only climbs, matching
        // CompletionState and LeaderboardAggregation.Best, which made the same call for the same
        // reason. Nothing to pay on the way down either way.
        if (xpAfter <= xpBefore) return [];

        var curve = await CurveAsync(cancellationToken);

        if (curve.Count == 0) return [];

        var crossed = new List<int>();

        foreach (var rung in curve)
        {
            // Strictly greater than "before" and at most "after": landing exactly on a threshold
            // reaches that level, and starting exactly on one does not re-award it.
            if (rung.CumulativeXp > xpBefore && rung.CumulativeXp <= xpAfter)
            {
                crossed.Add(rung.Level);
            }
        }

        return crossed;
    }

    public async Task<IReadOnlyList<LevelThresholdDto>> GetCurveAsync(
        CancellationToken cancellationToken = default)
    {
        var curve = await CurveAsync(cancellationToken);

        return curve
            .Select(t => new LevelThresholdDto { Level = t.Level, CumulativeXp = t.CumulativeXp })
            .ToList();
    }

    public async Task<ServiceResult<IReadOnlyList<LevelThresholdDto>>> ReplaceCurveAsync(
        ReplaceLevelCurveRequest request,
        CancellationToken cancellationToken = default)
    {
        var levels = request.Levels
            .OrderBy(l => l.Level)
            .ToList();

        // Every invariant below is a property of the whole set, which is why authoring replaces the
        // curve rather than editing a rung: none of these can be checked one row at a time.
        var errors = new List<string>();

        if (levels[0].Level != 1)
            errors.Add("The curve must start at level 1.");

        if (levels[0].CumulativeXp != 0)
            errors.Add("Level 1 must start at 0 XP — a player with no XP has to be somewhere.");

        for (var i = 1; i < levels.Count; i++)
        {
            if (levels[i].Level != levels[i - 1].Level + 1)
            {
                errors.Add(
                    $"Levels must be contiguous: level {levels[i - 1].Level} is followed by {levels[i].Level}.");
                break;
            }

            if (levels[i].CumulativeXp <= levels[i - 1].CumulativeXp)
            {
                errors.Add(
                    $"Level {levels[i].Level} must cost more than level {levels[i - 1].Level}; " +
                    "two levels starting at the same XP cannot be told apart.");
                break;
            }
        }

        if (errors.Count > 0)
        {
            return ServiceResult<IReadOnlyList<LevelThresholdDto>>.Invalid([.. errors]);
        }

        var now = DateTime.UtcNow;
        var existing = await _dbContext.LevelThresholds.ToListAsync(cancellationToken);
        var byLevel = existing.ToDictionary(t => t.Level);

        foreach (var entry in levels)
        {
            if (byLevel.TryGetValue(entry.Level, out var row))
            {
                if (row.CumulativeXp != entry.CumulativeXp)
                {
                    row.CumulativeXp = entry.CumulativeXp;
                    row.UpdatedAtUtc = now;
                }

                byLevel.Remove(entry.Level);
                continue;
            }

            _dbContext.LevelThresholds.Add(new LevelThreshold
            {
                Level = entry.Level,
                CumulativeXp = entry.CumulativeXp,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        // Whatever the new curve did not mention is a level that no longer exists. Shortening the
        // curve demotes nobody: the level is derived, so a player above the new cap simply reads as
        // the new maximum, and their XP is untouched and still there if the curve grows back.
        _dbContext.LevelThresholds.RemoveRange(byLevel.Values);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // The one write path, and therefore the one place invalidation belongs. Cleared after the
        // save, never before: a reader arriving mid-replace must see the old curve rather than
        // reload a half-written one.
        _curve.Invalidate();

        return ServiceResult<IReadOnlyList<LevelThresholdDto>>.Success(
            levels
                .Select(l => new LevelThresholdDto { Level = l.Level, CumulativeXp = l.CumulativeXp })
                .ToList());
    }

    // ---- internals ---------------------------------------------------------------------------



    private async Task<IReadOnlyList<LevelThreshold>> CurveAsync(CancellationToken cancellationToken)
    {
        if (_curve.Current is { } cached)
            return cached;

        var loaded = await _dbContext.LevelThresholds
            .AsNoTracking()
            .OrderBy(t => t.CumulativeXp)
            .ToListAsync(cancellationToken);

        // Two requests racing here both load and both publish the same rows; the loser's list is
        // simply dropped. A lock would serialise every cold read to save one duplicate query on a
        // few dozen rows, which is the wrong trade.
        _curve.Set(loaded);

        return loaded;
    }

    /// <summary>
    /// Index of the highest rung at or below <paramref name="xp"/>. Binary search rather than a
    /// scan — the curve is sorted, and this runs on the attempt hot path.
    /// </summary>
    private static int IndexFor(IReadOnlyList<LevelThreshold> curve, long xp)
    {
        var low = 0;
        var high = curve.Count - 1;
        var found = 0;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (curve[mid].CumulativeXp <= xp)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    private async Task<long> XpBalanceAsync(Guid userId, CancellationToken cancellationToken)
    {
        // A currency the account has never held has no row; that is zero, not an error.
        return await _dbContext.UserCurrencyBalances
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.CurrencyId == CurrencyIds.Xp)
            .Select(b => b.Amount)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
