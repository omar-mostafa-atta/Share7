using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.BrainPass;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Feed;
using Share7.Application.Rewards.Models;
using Share7.Domain.BrainPass;
using Share7.Domain.Constants;
using Share7.Domain.Leaderboards;
using Share7.Domain.Runs;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Social;

namespace Share7.Infrastructure.BrainPass;

public sealed class BrainPassService(ApplicationDbContext db, IBrainPassRewardService rewards,
    IPlayerEventPublisher feed, ILanguageService language) : IBrainPassService
{
    private const int Batch = 500;

    public async Task<ServiceResult<BrainPassDto?>> CurrentAsync(Guid user, CancellationToken token = default)
    {
        var now = DateTime.UtcNow;
        var id = await db.BrainPassSeasons.AsNoTracking().Where(s => s.State == BrainPassState.Published
            && s.StartsAtUtc <= now && s.ClaimUntilUtc > now).OrderByDescending(s => s.StartsAtUtc).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(token);
        if (id is null) return ServiceResult<BrainPassDto?>.Success(null);
        var result = await ReadAsync(user, id.Value, token);
        return result.Succeeded ? ServiceResult<BrainPassDto?>.Success(result.Value)
            : ServiceResult<BrainPassDto?>.Failure(result.Error ?? BrainPassErrors.NotFound, result.ErrorKind, "Season unavailable.");
    }

    public async Task<ServiceResult<BrainPassDto>> ReadAsync(Guid user, Guid seasonId, CancellationToken token = default)
    {
        var season = await db.BrainPassSeasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId && s.State != BrainPassState.Draft, token);
        if (season is null) return Failure<BrainPassDto>(BrainPassErrors.NotFound, ServiceErrorKind.NotFound);
        if (season.State == BrainPassState.Published) await ProjectAsync(user, season, token);
        var xp = await db.BrainPassProgress.AsNoTracking().Where(p => p.UserId == user && p.SeasonId == seasonId).Select(p => p.Xp).FirstOrDefaultAsync(token);
        var premium = await OwnsPremiumAsync(user, season, token);
        var claimed = await db.BrainPassClaims.AsNoTracking().Where(c => c.UserId == user && c.SeasonId == seasonId).Select(c => new { c.Tier, c.Track }).ToListAsync(token);
        var tiers = await db.BrainPassTiers.AsNoTracking().Where(t => t.SeasonId == seasonId).OrderBy(t => t.Number).ThenBy(t => t.Track).ToListAsync(token);
        var ruleIds = tiers.Select(t => t.RewardRuleId).Distinct().ToArray();
        var rules = await db.RewardRules.AsNoTracking().Include(r => r.Grants).ThenInclude(g => g.Currency)
            .Include(r => r.EntitlementGrants).ThenInclude(g => g.Product).Where(r => ruleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, token);
        var now = DateTime.UtcNow;
        var dtoTiers = tiers.Select(t =>
        {
            var rule = rules[t.RewardRuleId]; var paid = claimed.Any(c => c.Tier == t.Number && c.Track == t.Track);
            return new BrainPassTierDto(t.Number, t.Track, t.RequiredXp, xp >= t.RequiredXp, paid,
                !paid && xp >= t.RequiredXp && season.State == BrainPassState.Published && now >= season.StartsAtUtc
                && now < season.ClaimUntilUtc && (t.Track == BrainPassTrack.Free || premium)
                && rule.Enabled && rule.Grants.All(g => g.Currency!.Enabled) && rule.EntitlementGrants.All(g => g.Product!.Active),
                rule.Grants.Select(g => new RewardGrantDto { Currency = g.Currency!.Key, Amount = g.Amount }).ToArray(),
                rule.EntitlementGrants.Select(g => new RewardEntitlementDto { ProductId = g.ProductId, ProductKey = g.Product!.Key }).ToArray());
        }).ToArray();
        return ServiceResult<BrainPassDto>.Success(new(seasonId, season.Key,
            await language.ResolveForUserAsync(user, token) == LanguageIds.Arabic ? season.NameAr : season.NameEn,
            season.State, Utc(season.StartsAtUtc), Utc(season.EndsAtUtc), Utc(season.ClaimUntilUtc), xp, premium,
            season.ObjectiveGroupKey, dtoTiers, season.State == BrainPassState.Published && await Pending(user, season).AnyAsync(token), now));
    }

    public async Task<ServiceResult<BrainPassClaimDto>> ClaimAsync(Guid user, Guid seasonId, int tier, BrainPassTrack track, CancellationToken token = default)
    {
        if (!Enum.IsDefined(track) || tier is < 1 or > 100) return Failure<BrainPassClaimDto>(BrainPassErrors.Locked, ServiceErrorKind.Validation);
        var snapshot = await db.BrainPassSeasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == seasonId && s.State != BrainPassState.Draft, token);
        if (snapshot is null) return Failure<BrainPassClaimDto>(BrainPassErrors.NotFound, ServiceErrorKind.NotFound);
        if (snapshot.State == BrainPassState.Published) await ProjectAsync(user, snapshot, token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        await SeasonReadLockAsync(seasonId, token);
        var claim = await db.BrainPassClaims.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == user && c.SeasonId == seasonId && c.Tier == tier && c.Track == track, token);
        // A committed claim remains recoverable after expiry or the operator's kill switch.
        if (claim is not null) return ServiceResult<BrainPassClaimDto>.Success(new(tier, track,
            JsonSerializer.Deserialize<RewardDto[]>(claim.RewardsJson) ?? [], true));
        var season = await db.BrainPassSeasons.AsNoTracking().FirstAsync(s => s.Id == seasonId, token);
        var now = DateTime.UtcNow;
        if (season.State != BrainPassState.Published || now < season.StartsAtUtc || now >= season.ClaimUntilUtc)
            return Failure<BrainPassClaimDto>(BrainPassErrors.Closed, ServiceErrorKind.Conflict);
        var definition = await db.BrainPassTiers.AsNoTracking().FirstOrDefaultAsync(t => t.SeasonId == seasonId && t.Number == tier && t.Track == track, token);
        var xp = await db.BrainPassProgress.AsNoTracking().Where(p => p.UserId == user && p.SeasonId == seasonId).Select(p => p.Xp).FirstOrDefaultAsync(token);
        if (definition is null || xp < definition.RequiredXp) return Failure<BrainPassClaimDto>(BrainPassErrors.Locked, ServiceErrorKind.Conflict);
        if (track == BrainPassTrack.Premium && !await OwnsPremiumAsync(user, season, token))
            return Failure<BrainPassClaimDto>(BrainPassErrors.Premium, ServiceErrorKind.Forbidden);
        var payout = await rewards.EvaluateBrainPassAsync(new(user, seasonId, tier, track, definition.RewardRuleId), token);
        if (payout.Count == 0) return Failure<BrainPassClaimDto>(BrainPassErrors.RewardUnavailable, ServiceErrorKind.Conflict);
        db.BrainPassClaims.Add(new() { SeasonId = seasonId, UserId = user, Tier = tier, Track = track,
            RewardsJson = JsonSerializer.Serialize(payout), ClaimedAtUtc = now });
        feed.Stage(user, "brainpass.reward.claimed", new { seasonId, tier, track });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return ServiceResult<BrainPassClaimDto>.Success(new(tier, track, payout, false));
    }

    public async Task<int> ProjectPendingAsync(CancellationToken token = default)
    {
        var now = DateTime.UtcNow;
        var pending = await (from season in db.BrainPassSeasons.AsNoTracking()
            from result in db.GameResults.AsNoTracking()
            where season.State == BrainPassState.Published && season.StartsAtUtc <= now
                && result.OccurredAtUtc >= season.StartsAtUtc && result.OccurredAtUtc < season.EndsAtUtc
                && !db.BrainPassCredits.Any(c => c.SeasonId == season.Id && c.ResultId == result.Id)
            group result by new { Season = season.Id, result.UserId } into batch
            orderby batch.Min(r => r.Sequence)
            select batch.Key).Take(20).ToListAsync(token);
        var count = 0;
        foreach (var pair in pending)
        {
            var season = await db.BrainPassSeasons.AsNoTracking().FirstAsync(s => s.Id == pair.Season, token);
            count += await ProjectAsync(pair.UserId, season, token);
            // Each pair is an independent transaction; release tracked state before the next account.
            db.ChangeTracker.Clear();
        }
        return count;
    }

    private IQueryable<GameResult> Pending(Guid user, BrainPassSeason season) => db.GameResults.AsNoTracking()
        .Where(r => r.UserId == user && r.OccurredAtUtc >= season.StartsAtUtc && r.OccurredAtUtc < season.EndsAtUtc
            && !db.BrainPassCredits.Any(c => c.SeasonId == season.Id && c.ResultId == r.Id));

    private async Task<int> ProjectAsync(Guid user, BrainPassSeason snapshot, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        await SeasonReadLockAsync(snapshot.Id, token);
        var season = await db.BrainPassSeasons.AsNoTracking().FirstOrDefaultAsync(s => s.Id == snapshot.Id && s.State == BrainPassState.Published, token);
        if (season is null) return 0;
        var results = await Pending(user, season).OrderBy(r => r.Sequence).Take(Batch).ToListAsync(token);
        if (results.Count == 0) return 0;
        var rules = await db.BrainPassXpRules.AsNoTracking().Where(r => r.SeasonId == season.Id).ToDictionaryAsync(r => r.Metric, token);
        var sourceIds = results.Select(r => r.SourceId).Distinct().ToArray();
        var sources = await db.BrainPassSources.Where(s => s.SeasonId == season.Id && s.UserId == user && sourceIds.Contains(s.SourceId))
            .ToDictionaryAsync(s => (s.Metric, s.SourceId), token);
        var days = results.Select(r => r.OccurredAtUtc.Date).Distinct().ToArray();
        var daily = await db.BrainPassDaily.Where(d => d.SeasonId == season.Id && d.UserId == user && days.Contains(d.DayUtc))
            .ToDictionaryAsync(d => (d.Metric, d.DayUtc), token);
        var validRuns = await db.Runs.AsNoTracking().Where(r => r.UserId == user && sourceIds.Contains(r.Id)
            && r.State == RunState.Settled && r.Outcome == RunOutcome.Completed && !r.IsFlagged && r.DurationMs >= 60000)
            .Select(r => r.Id).ToListAsync(token);
        var validMatches = await db.MatchPlacements.AsNoTracking().Where(p => p.UserId == user && sourceIds.Contains(p.SessionId)
            && !p.Forfeited && !p.Flagged && p.Result!.ReportedCount >= 2
            && (db.MatchAttemptScores.Any(a => a.SessionId == p.SessionId && a.UserId == user && a.CorrectCount > 0)
                || db.Runs.Any(r => r.SessionId == p.SessionId && r.UserId == user && r.State == RunState.Settled
                    && r.Outcome == RunOutcome.Completed && !r.IsFlagged && r.DurationMs >= 60000)))
            .Select(p => p.SessionId).ToListAsync(token);
        var progress = await db.BrainPassProgress.FirstOrDefaultAsync(p => p.UserId == user && p.SeasonId == season.Id, token);
        if (progress is null) { progress = new() { UserId = user, SeasonId = season.Id }; db.BrainPassProgress.Add(progress); }
        foreach (var result in results)
        {
            var xp = 0;
            if (!result.IsFlagged && result.Value > 0 && rules.TryGetValue(result.Metric, out var rule)
                && ((result.Metric.StartsWith("LESSON", StringComparison.Ordinal) && result.SourceType == GameResultSource.Attempt)
                    || (result.Metric == "RUN_SECONDS" && result.SourceType == GameResultSource.Session && validRuns.Contains(result.SourceId))
                    || (result.Metric == "MATCHES_PLAYED" && result.SourceType == GameResultSource.Session && validMatches.Contains(result.SourceId))))
            {
                var key = (result.Metric, result.SourceId);
                if (!sources.TryGetValue(key, out var source))
                {
                    source = new() { UserId = user, SeasonId = season.Id, Metric = result.Metric, SourceId = result.SourceId };
                    sources.Add(key, source); db.BrainPassSources.Add(source);
                }
                var measured = Math.Min(result.Value, result.Metric == "LESSON_BEST_PERCENT" ? 100L
                    : result.Metric is "LESSONS_COMPLETED" or "LESSONS_ACED" or "MATCHES_PLAYED" ? 1L : 86400L);
                if (measured >= rule.MinimumValue && measured > source.MaximumValue)
                {
                    // Cap before multiplication, including extreme operator values and source improvements.
                    static int Potential(long value, BrainPassXpRule rule) => value < rule.MinimumValue ? 0 : (int)Math.Min(rule.MaxSourceXp,
                        Math.Min(value / rule.UnitValue, rule.MaxSourceXp) * rule.XpPerUnit);
                    var wanted = Math.Max(0, Potential(measured, rule) - Potential(source.MaximumValue, rule));
                    var dayKey = (result.Metric, result.OccurredAtUtc.Date);
                    if (!daily.TryGetValue(dayKey, out var day))
                    {
                        day = new() { SeasonId = season.Id, UserId = user, Metric = result.Metric, DayUtc = dayKey.Item2 };
                        daily.Add(dayKey, day); db.BrainPassDaily.Add(day);
                    }
                    xp = Math.Min(wanted, Math.Max(0, rule.DailyCap - day.Xp));
                    day.Xp += xp; source.CreditedXp += xp;
                }
                // Consumed source progress cannot be replayed tomorrow after today's cap was reached.
                source.MaximumValue = Math.Max(source.MaximumValue, measured);
            }
            db.BrainPassCredits.Add(new() { SeasonId = season.Id, UserId = user, ResultId = result.Id, Xp = xp, CreatedAtUtc = DateTime.UtcNow });
            progress.Xp += xp;
        }
        progress.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return results.Count;
    }

    private async Task SeasonReadLockAsync(Guid id, CancellationToken token) => await db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT [Id] FROM [BrainPassSeasons] WITH (HOLDLOCK, ROWLOCK) WHERE [Id] = {id}", token);
    private Task<bool> OwnsPremiumAsync(Guid user, BrainPassSeason season, CancellationToken token) => season.PremiumProductId is { } id
        ? db.Entitlements.AnyAsync(e => e.UserId == user && e.ProductId == id, token) : Task.FromResult(false);
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static ServiceResult<T> Failure<T>(ApiErrorCode code, ServiceErrorKind kind) => ServiceResult<T>.Failure(code, kind, code.Code);
}
