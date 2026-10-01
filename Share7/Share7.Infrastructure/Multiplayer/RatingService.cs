using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Feed;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Feed;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Ratings and seasonal ranks. See <see cref="IRatingService"/>.
/// <para>
/// <b>Concurrency.</b> Two matches a player was in can be decided at the same instant — one by its
/// last report, one by the sweeper's deadline. Every rating row a match touches is locked first, in
/// user order, so the two updates queue rather than one overwriting the other or the pair
/// deadlocking.
/// </para>
/// </summary>
public class RatingService : IRatingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPlayerEventPublisher _events;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<RatingService> _logger;

    public RatingService(
        ApplicationDbContext dbContext,
        IPlayerEventPublisher events,
        IOptions<MultiplayerOptions> options,
        ILogger<RatingService> logger)
    {
        _dbContext = dbContext;
        _events = events;
        _options = options.Value;
        _logger = logger;
    }

    // ---- applying a match ----------------------------------------------------------------------

    public async Task<IReadOnlyDictionary<Guid, int>> ApplyAsync(
        Guid sessionId, Guid modeId, IReadOnlyList<RatedPlacement> placements, CancellationToken cancellationToken = default)
    {
        var tiersReached = new Dictionary<Guid, int>();

        if (placements.Count < 2)
            return tiersReached;

        // Rated once: the change rows are keyed by the match, and the verdict that calls this is
        // itself written once — this is the belt to that brace.
        if (await _dbContext.PlayerRatingChanges.AnyAsync(c => c.SessionId == sessionId, cancellationToken))
            return tiersReached;

        var now = DateTime.UtcNow;
        var season = RankedSeasons.KeyFor(now);
        var players = placements.OrderBy(p => p.UserId).ToList();

        // Lock (and, for a first match, create) every rating row in user order before reading any.
        foreach (var player in players)
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM [PlayerRatings] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {0} AND [ModeId] = {1})
                    INSERT INTO [PlayerRatings] ([UserId], [ModeId], [Mu], [Sigma], [MatchesPlayed], [SeasonKey], [UpdatedAtUtc])
                    VALUES ({0}, {1}, {2}, {3}, 0, {4}, {5});
                """,
                [player.UserId, modeId, RatingModel.InitialMu, RatingModel.InitialSigma, season, now],
                cancellationToken);
        }

        var ids = players.Select(p => p.UserId).ToList();

        var stored = await _dbContext.PlayerRatings.AsNoTracking()
            .Where(r => r.ModeId == modeId && ids.Contains(r.UserId))
            .ToDictionaryAsync(r => r.UserId, cancellationToken);

        // A new season softens what is known, so its placements mean something — once per player per season.
        var before = players.ToDictionary(p => p.UserId, p =>
        {
            var row = stored[p.UserId];
            var sigma = row.MatchesPlayed > 0 && row.SeasonKey != season
                ? Math.Min(RatingModel.InitialSigma, Math.Sqrt(row.Sigma * row.Sigma + _options.RankedSeasonSigmaBump * _options.RankedSeasonSigmaBump))
                : row.Sigma;

            return new Rating(row.Mu, sigma);
        });

        var rated = RatingModel.Rate(players.Select(p => (before[p.UserId], p.Placement)).ToList());
        var skipped = await RepeatOpponentsAsync(ids, now, cancellationToken);

        // A walkover — fewer than two players reported — credits nobody: otherwise a second account that
        // joins and quits is a way to farm. The player who walked away still loses, so leaving is never free.
        var walkover = players.Count(p => !p.Forfeited) < 2;

        var standings = await LockStandingsAsync(ids, modeId, season, now, cancellationToken);

        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var prior = before[player.UserId];
            var reason = walkover && !player.Forfeited ? "walkover"
                : skipped.Contains(player.UserId) ? "repeat_opponent"
                : null;
            var after = reason is null ? rated[i] : prior;

            await _dbContext.PlayerRatings
                .Where(r => r.UserId == player.UserId && r.ModeId == modeId)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.Mu, after.Mu)
                    .SetProperty(r => r.Sigma, after.Sigma)
                    .SetProperty(r => r.MatchesPlayed, r => r.MatchesPlayed + 1)
                    .SetProperty(r => r.SeasonKey, season)
                    .SetProperty(r => r.UpdatedAtUtc, now), cancellationToken);

            var standing = standings[player.UserId];

            // A match that moved nothing adds no visible win either.
            var win = player.IsWinner && reason is null ? 1 : 0;
            var played = standing.MatchesPlayed + 1;
            var placed = played >= _options.RankedPlacementMatches;
            double? peak = placed ? Math.Max(standing.PeakOrdinal ?? double.MinValue, after.Ordinal) : null;

            var promoted = standing.PeakOrdinal is { } oldPeak && peak is { } newPeak
                           && RankedTiers.Step(newPeak) > RankedTiers.Step(oldPeak);

            // A tier reached for the first time this season — placements just ended, or a promotion
            // crossed into the next tier (not merely the next division).
            if (peak is { } reachedPeak
                && RankedTiers.TierNumber(reachedPeak) > (standing.PeakOrdinal is { } previous ? RankedTiers.TierNumber(previous) : 0))
                tiersReached[player.UserId] = RankedTiers.TierNumber(reachedPeak);

            await _dbContext.RankedSeasonStandings
                .Where(s => s.UserId == player.UserId && s.ModeId == modeId && s.SeasonKey == season)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.MatchesPlayed, played)
                    .SetProperty(s => s.Wins, s => s.Wins + win)
                    .SetProperty(s => s.PeakOrdinal, peak)
                    .SetProperty(s => s.UpdatedAtUtc, now), cancellationToken);

            _dbContext.PlayerRatingChanges.Add(new PlayerRatingChange
            {
                SessionId = sessionId,
                UserId = player.UserId,
                ModeId = modeId,
                Placement = player.Placement,
                MuBefore = prior.Mu,
                SigmaBefore = prior.Sigma,
                MuAfter = after.Mu,
                SigmaAfter = after.Sigma,
                SkippedReason = reason,
                SeasonKey = season,
                Promoted = promoted,
                CreatedAtUtc = now
            });

            var view = View(played, peak);

            _events.Stage(player.UserId, PlayerEventTypes.RankedUpdated, new
            {
                sessionId,
                modeId,
                seasonKey = season,
                view.IsPlacement,
                view.PlacementMatchesLeft,
                view.Tier,
                view.Division,
                promoted,
                notCountedReason = reason
            }, now.AddDays(1));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (skipped.Count > 0)
            _logger.LogInformation("Rated match {SessionId}: {Count} player(s) not moved (repeat opponents).", sessionId, skipped.Count);

        return tiersReached;
    }

    /// <summary>
    /// Players who have already met one of this match's other players too often today. Their rating
    /// stays where it is for this match — the cheapest defence against two accounts farming each
    /// other, and harmless to two friends who simply keep meeting.
    /// </summary>
    private async Task<HashSet<Guid>> RepeatOpponentsAsync(IReadOnlyList<Guid> ids, DateTime now, CancellationToken cancellationToken)
    {
        var since = now.AddDays(-1);

        var recent = await _dbContext.PlayerRatingChanges.AsNoTracking()
            .Where(c => ids.Contains(c.UserId) && c.CreatedAtUtc >= since)
            .Select(c => new { c.SessionId, c.UserId })
            .ToListAsync(cancellationToken);

        var meetings = new Dictionary<(Guid, Guid), int>();

        foreach (var match in recent.GroupBy(c => c.SessionId))
        {
            var present = match.Select(c => c.UserId).ToList();

            foreach (var a in present)
            foreach (var b in present.Where(b => b != a))
                meetings[(a, b)] = meetings.GetValueOrDefault((a, b)) + 1;
        }

        return ids
            .Where(a => ids.Any(b => b != a && meetings.GetValueOrDefault((a, b)) >= _options.RankedRepeatOpponentLimitPerDay))
            .ToHashSet();
    }

    /// <summary>This season's standing rows for the match's players, created and locked in user order.</summary>
    private async Task<Dictionary<Guid, RankedSeasonStanding>> LockStandingsAsync(
        IReadOnlyList<Guid> ids, Guid modeId, string season, DateTime now, CancellationToken cancellationToken)
    {
        foreach (var userId in ids.OrderBy(id => id))
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM [RankedSeasonStandings] WITH (UPDLOCK, HOLDLOCK)
                               WHERE [UserId] = {0} AND [ModeId] = {1} AND [SeasonKey] = {2})
                    INSERT INTO [RankedSeasonStandings] ([UserId], [ModeId], [SeasonKey], [MatchesPlayed], [Wins], [PeakOrdinal], [UpdatedAtUtc])
                    VALUES ({0}, {1}, {2}, 0, 0, NULL, {3});
                """,
                [userId, modeId, season, now],
                cancellationToken);
        }

        return await _dbContext.RankedSeasonStandings.AsNoTracking()
            .Where(s => s.ModeId == modeId && s.SeasonKey == season && ids.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, cancellationToken);
    }

    // ---- reads ---------------------------------------------------------------------------------

    public async Task<IReadOnlyDictionary<Guid, Rating>> RatingsAsync(
        IReadOnlyCollection<Guid> userIds, Guid modeId, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();

        var stored = await _dbContext.PlayerRatings.AsNoTracking()
            .Where(r => r.ModeId == modeId && ids.Contains(r.UserId))
            .ToDictionaryAsync(r => r.UserId, r => new Rating(r.Mu, r.Sigma), cancellationToken);

        return ids.ToDictionary(id => id, id => stored.GetValueOrDefault(id, RatingModel.Initial));
    }

    public async Task<ServiceResult<RankedStandingDto>> StandingAsync(Guid userId, Guid modeId, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.GameModes.AnyAsync(m => m.Id == modeId, cancellationToken))
            return ServiceResult<RankedStandingDto>.Failure(ApiErrors.PlayModeUnknown, ServiceErrorKind.NotFound, "No mode has that id.");

        var season = RankedSeasons.KeyFor(DateTime.UtcNow);

        var standing = await _dbContext.RankedSeasonStandings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.ModeId == modeId && s.SeasonKey == season, cancellationToken);

        var view = View(standing?.MatchesPlayed ?? 0, standing?.PeakOrdinal);

        return ServiceResult<RankedStandingDto>.Success(new RankedStandingDto
        {
            ModeId = modeId,
            SeasonKey = season,
            SeasonEndsAtUtc = RankedSeasons.EndOf(season),
            IsPlacement = view.IsPlacement,
            PlacementMatchesLeft = view.PlacementMatchesLeft,
            Tier = view.Tier,
            Division = view.Division,
            MatchesPlayed = standing?.MatchesPlayed ?? 0,
            Wins = standing?.Wins ?? 0,
            ServerTimeUtc = DateTime.UtcNow
        });
    }

    public async Task<RankedResultDto?> ResultForAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var change = await _dbContext.PlayerRatingChanges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SessionId == sessionId && c.UserId == userId, cancellationToken);

        if (change is null)
            return null;

        var standing = await _dbContext.RankedSeasonStandings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.ModeId == change.ModeId && s.SeasonKey == change.SeasonKey, cancellationToken);

        var view = View(standing?.MatchesPlayed ?? 0, standing?.PeakOrdinal);

        return new RankedResultDto
        {
            ModeId = change.ModeId,
            SeasonKey = change.SeasonKey,
            IsPlacement = view.IsPlacement,
            PlacementMatchesLeft = view.PlacementMatchesLeft,
            Tier = view.Tier,
            Division = view.Division,
            Promoted = change.Promoted,
            NotCountedReason = change.SkippedReason
        };
    }

    private sealed record StandingView(bool IsPlacement, int PlacementMatchesLeft, string? Tier, int? Division);

    private StandingView View(int played, double? peak)
    {
        var left = Math.Max(0, _options.RankedPlacementMatches - played);

        if (left > 0 || peak is null)
            return new StandingView(true, left, null, null);

        var (tier, division) = RankedTiers.For(peak.Value);
        return new StandingView(false, 0, tier, division);
    }
}
