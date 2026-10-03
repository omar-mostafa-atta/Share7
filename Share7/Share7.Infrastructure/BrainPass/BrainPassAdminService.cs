using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.BrainPass;
using Share7.Application.Common.Models;
using Share7.Domain.BrainPass;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Social;

namespace Share7.Infrastructure.BrainPass;

public sealed class BrainPassAdminService(ApplicationDbContext db, IAuditLog audit) : IBrainPassAdminService
{
    internal static readonly HashSet<string> Metrics = ["LESSONS_COMPLETED", "LESSONS_ACED", "LESSON_BEST_PERCENT", "RUN_SECONDS", "MATCHES_PLAYED"];

    public async Task<IReadOnlyList<BrainPassAdminDto>> ListAsync(CancellationToken token = default)
    {
        var seasons = await db.BrainPassSeasons.AsNoTracking().OrderByDescending(s => s.StartsAtUtc).Take(100).ToListAsync(token);
        var ids = seasons.Select(s => s.Id).ToArray();
        var tiers = await db.BrainPassTiers.AsNoTracking().Where(t => ids.Contains(t.SeasonId)).ToListAsync(token);
        var rules = await db.BrainPassXpRules.AsNoTracking().Where(r => ids.Contains(r.SeasonId)).ToListAsync(token);
        return seasons.Select(s => Map(s, rules.Where(r => r.SeasonId == s.Id), tiers.Where(t => t.SeasonId == s.Id))).ToArray();
    }

    public async Task<ServiceResult<BrainPassAdminDto>> SaveAsync(Guid? id, BrainPassSeasonInput request, CancellationToken token = default)
    {
        if (!Valid(request) || !await ValidRewardsAsync(request, token)) return Invalid();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await AuthoringLockAsync(token);
        BrainPassSeason? season = null;
        if (id.HasValue)
        {
            season = await db.BrainPassSeasons.FirstOrDefaultAsync(s => s.Id == id, token);
            if (season is null) return ServiceResult<BrainPassAdminDto>.NotFound("Season unavailable.");
            if (season.State != BrainPassState.Draft || season.Version != request.ExpectedVersion)
                return ServiceResult<BrainPassAdminDto>.Conflict("Published seasons are immutable; reload the current draft.");
        }
        else if (request.ExpectedVersion != 0) return Invalid();
        if (await db.BrainPassSeasons.AnyAsync(s => s.Key == request.Key && (!id.HasValue || s.Id != id.Value), token))
            return ServiceResult<BrainPassAdminDto>.Conflict("Season key already exists.");
        if (season is null)
        {
            season = new() { Id = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow, State = BrainPassState.Draft };
            db.BrainPassSeasons.Add(season);
        }
        else
        {
            await db.BrainPassTiers.Where(t => t.SeasonId == season.Id).ExecuteDeleteAsync(token);
            await db.BrainPassXpRules.Where(r => r.SeasonId == season.Id).ExecuteDeleteAsync(token);
        }
        season.Key = request.Key; season.NameEn = request.NameEn.Trim(); season.NameAr = request.NameAr.Trim();
        season.StartsAtUtc = GamingProfileService.Utc(request.StartsAtUtc); season.EndsAtUtc = GamingProfileService.Utc(request.EndsAtUtc);
        season.ClaimUntilUtc = GamingProfileService.Utc(request.ClaimUntilUtc); season.PremiumProductId = request.PremiumProductId;
        season.ObjectiveGroupKey = request.ObjectiveGroupKey; season.Version++;
        var rules = request.Rules.Select(r => new BrainPassXpRule { SeasonId = season.Id, Metric = r.Metric,
            MinimumValue = r.MinimumValue, UnitValue = r.UnitValue, XpPerUnit = r.XpPerUnit, MaxSourceXp = r.MaxSourceXp, DailyCap = r.DailyCap }).ToArray();
        var tiers = request.Tiers.Select(t => new BrainPassTier { SeasonId = season.Id, Number = t.Number, Track = t.Track,
            RequiredXp = t.RequiredXp, RewardRuleId = t.RewardRuleId }).ToArray();
        db.BrainPassXpRules.AddRange(rules); db.BrainPassTiers.AddRange(tiers);
        audit.Record(new("brainpass.draft.saved", "brainpass", "Saved a seasonal progression draft.", "season", season.Id.ToString(), new { season.Version }));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return ServiceResult<BrainPassAdminDto>.Success(Map(season, rules, tiers));
    }

    public async Task<ServiceResult<BrainPassAdminDto>> PublishAsync(Guid id, int expectedVersion, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await AuthoringLockAsync(token);
        var season = await db.BrainPassSeasons.FirstOrDefaultAsync(s => s.Id == id, token);
        if (season is null) return ServiceResult<BrainPassAdminDto>.NotFound("Season unavailable.");
        var rules = await db.BrainPassXpRules.Where(r => r.SeasonId == id).ToListAsync(token);
        var tiers = await db.BrainPassTiers.Where(t => t.SeasonId == id).ToListAsync(token);
        if (season.State == BrainPassState.Published && season.Version == expectedVersion + 1)
            return ServiceResult<BrainPassAdminDto>.Success(Map(season, rules, tiers));
        if (season.Version != expectedVersion) return ServiceResult<BrainPassAdminDto>.Conflict("Reload the saved draft before publishing.");
        if (season.State != BrainPassState.Draft || season.StartsAtUtc <= DateTime.UtcNow) return Invalid();
        var configuration = Map(season, rules, tiers).Configuration;
        if (!Valid(configuration) || !await ValidRewardsAsync(configuration, token)) return Invalid();
        if (await db.BrainPassSeasons.AnyAsync(s => s.Id != id && s.State == BrainPassState.Published
            && s.StartsAtUtc < season.EndsAtUtc && s.EndsAtUtc > season.StartsAtUtc, token))
            return ServiceResult<BrainPassAdminDto>.Conflict("Published earning windows cannot overlap.");
        season.State = BrainPassState.Published; season.Version++;
        audit.Record(new("brainpass.published", "brainpass", "Published an immutable season.", "season", id.ToString(), new { season.Version }));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return ServiceResult<BrainPassAdminDto>.Success(Map(season, rules, tiers));
    }

    public async Task<ServiceResult> DisableAsync(Guid id, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await AuthoringLockAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [BrainPassSeasons] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}", token);
        var season = await db.BrainPassSeasons.FirstOrDefaultAsync(s => s.Id == id, token);
        if (season is null) return ServiceResult.NotFound("Season unavailable.");
        if (season.State == BrainPassState.Disabled) return ServiceResult.Success();
        season.State = BrainPassState.Disabled; season.Version++;
        audit.Record(new("brainpass.disabled", "brainpass", "Paused seasonal earning and new claims; preserved existing claims.", "season", id.ToString()));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }

    private async Task AuthoringLockAsync(CancellationToken token) => await db.Database.ExecuteSqlRawAsync(
        "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'Share7.BrainPass.Authoring', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result < 0 THROW 51000, 'Season authoring lock unavailable', 1;", token);

    internal static bool Valid(BrainPassSeasonInput r)
    {
        if (!SocialProfileAdminService.Key(r.Key, 64) || !SocialProfileAdminService.Text(r.NameEn, 80)
            || !SocialProfileAdminService.Text(r.NameAr, 80) || r.EndsAtUtc <= r.StartsAtUtc
            || r.EndsAtUtc - r.StartsAtUtc > TimeSpan.FromDays(366) || r.ClaimUntilUtc < r.EndsAtUtc
            || r.ClaimUntilUtc - r.EndsAtUtc > TimeSpan.FromDays(30)
            || (r.ObjectiveGroupKey is not null && !SocialProfileAdminService.Key(r.ObjectiveGroupKey, 64))
            || r.Rules is null || r.Tiers is null || r.Rules.Count is < 1 or > 5 || r.Tiers.Count is < 1 or > 200) return false;
        if (r.Rules.Select(x => x.Metric).Distinct().Count() != r.Rules.Count || r.Rules.Any(x => !Metrics.Contains(x.Metric)
            || x.MinimumValue < 1 || x.UnitValue < 1 || x.XpPerUnit is < 1 or > 10000
            || x.MaxSourceXp is < 1 or > 10000 || x.DailyCap is < 1 or > 10000
            || (x.Metric == "RUN_SECONDS" && (x.MinimumValue < 60 || x.UnitValue < 60)))) return false;
        if (r.Tiers.Any(t => t.Number is < 1 or > 100 || !Enum.IsDefined(t.Track) || t.RequiredXp is < 1 or > 10000000)
            || r.Tiers.Select(t => (t.Number, t.Track)).Distinct().Count() != r.Tiers.Count) return false;
        var free = r.Tiers.Where(t => t.Track == BrainPassTrack.Free).OrderBy(t => t.Number).ToArray();
        if (free.Length == 0 || free.Where((t, i) => t.Number != i + 1 || (i > 0 && t.RequiredXp <= free[i - 1].RequiredXp)).Any()) return false;
        return r.Tiers.Where(t => t.Track == BrainPassTrack.Premium).All(t => r.PremiumProductId.HasValue
            && free.Any(f => f.Number == t.Number && f.RequiredXp == t.RequiredXp));
    }

    private async Task<bool> ValidRewardsAsync(BrainPassSeasonInput r, CancellationToken token)
    {
        if (r.PremiumProductId is { } product && !await db.Products.AnyAsync(p => p.Id == product && p.Active, token)) return false;
        var ids = r.Tiers.Select(t => t.RewardRuleId).Distinct().ToArray();
        return await db.RewardRules.CountAsync(rule => ids.Contains(rule.Id) && rule.Enabled && rule.EventType == RewardEventType.BrainPassTier
            && rule.RepeatPolicy == RewardRepeatPolicy.Once && rule.ReferenceKey == null
            && (rule.Grants.Any() || rule.EntitlementGrants.Any())
            && !rule.Grants.Any(g => g.Amount <= 0 || !g.Currency!.Enabled)
            && !rule.EntitlementGrants.Any(g => !g.Product!.Active), token) == ids.Length;
    }

    internal static BrainPassAdminDto Map(BrainPassSeason s, IEnumerable<BrainPassXpRule> rules, IEnumerable<BrainPassTier> tiers) => new(s.Id, s.State, s.Version,
        new(s.Key, s.NameEn, s.NameAr, GamingProfileService.Utc(s.StartsAtUtc), GamingProfileService.Utc(s.EndsAtUtc), GamingProfileService.Utc(s.ClaimUntilUtc), s.PremiumProductId, s.ObjectiveGroupKey,
            rules.OrderBy(r => r.Metric).Select(r => new BrainPassRuleInput(r.Metric, r.MinimumValue, r.UnitValue, r.XpPerUnit, r.MaxSourceXp, r.DailyCap)).ToArray(),
            tiers.OrderBy(t => t.Number).ThenBy(t => t.Track).Select(t => new BrainPassTierInput(t.Number, t.Track, t.RequiredXp, t.RewardRuleId)).ToArray(), s.Version));
    private static ServiceResult<BrainPassAdminDto> Invalid() => ServiceResult<BrainPassAdminDto>.Failure(BrainPassErrors.Invalid, ServiceErrorKind.Validation, "Invalid season configuration.");
}
