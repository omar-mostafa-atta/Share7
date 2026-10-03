using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Social;
using Share7.Domain.Constants;
using Share7.Domain.Progress;
using Share7.Domain.Social;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Social;

public sealed class GamingProfileService(ApplicationDbContext db, ISocialPolicy policy, IBlockList blocks,
    IDisplayNameService names, IPresenceReader presence, ILanguageService language) : IGamingProfileService
{
    public async Task<ServiceResult<GamingProfileDto>> ReadAsync(Guid caller, Guid target, CancellationToken token = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == caller, token) || !await db.Users.AnyAsync(u => u.Id == target, token)) return Unavailable();
        if (caller != target)
        {
            if (await blocks.IsBlockedEitherWayAsync(caller, target, token)) return Unavailable();
            var official = await db.OfficialProfiles.AnyAsync(p => p.UserId == target && p.Discoverable, token);
            if (!official && !(await policy.CanInteractAsync(caller, target, SocialAction.SeeProfile, token)).Allowed) return Unavailable();
            if (await db.SocialPrivacy.AnyAsync(p => p.UserId == target && p.Profile == SocialVisibility.Nobody, token)) return Unavailable();
        }
        return ServiceResult<GamingProfileDto>.Success((await BuildAsync(caller, [target], token))[0]);
    }

    public async Task<ServiceResult<CursorPage<GamingProfileDto>>> DirectoryAsync(Guid caller, long after, CancellationToken token = default)
    {
        if (after < 0) return ServiceResult<CursorPage<GamingProfileDto>>.Invalid("Invalid cursor.");
        var blocked = (await blocks.BlockedEitherWayAsync(caller, token)).ToArray();
        var rows = await db.OfficialProfiles.AsNoTracking().Where(p => p.Discoverable && p.Sequence > after
            && !blocked.Contains(p.UserId) && !db.SocialPrivacy.Any(v => v.UserId == p.UserId && v.Profile == SocialVisibility.Nobody))
            .OrderBy(p => p.Sequence).Take(51).Select(p => new { p.UserId, p.Sequence }).ToListAsync(token);
        var page = rows.Take(50).ToArray();
        var dtos = await BuildAsync(caller, page.Select(p => p.UserId).ToArray(), token);
        return ServiceResult<CursorPage<GamingProfileDto>>.Success(new(dtos, rows.Count > 50 ? page[^1].Sequence : null, DateTime.UtcNow));
    }

    // Fixed query count for a directory page: no per-profile statistics/asset/presence lookups.
    private async Task<List<GamingProfileDto>> BuildAsync(Guid caller, Guid[] ids, CancellationToken token)
    {
        if (ids.Length == 0) return [];
        var arabic = await language.ResolveForUserAsync(caller, token) == LanguageIds.Arabic;
        var handles = await names.EnsureHandlesAsync(ids, token); // Never the configurable real-name roster source.
        var officials = await db.OfficialProfiles.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, token);
        var privacy = await db.SocialPrivacy.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, token);
        var outfits = await db.Equipments.AsNoTracking().Where(e => ids.Contains(e.UserId)).ToListAsync(token);
        var selections = await db.PlayerShowcases.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, token);
        var keys = selections.Values.SelectMany(p => Selection(p.SelectionJson)).Distinct().ToArray();
        var content = await db.ShowcaseContents.AsNoTracking().Where(c => keys.Contains(c.Key) && c.Enabled).ToDictionaryAsync(c => c.Key, token);
        var products = content.Values.Where(c => c.ProductId != null).Select(c => c.ProductId!.Value).Distinct().ToArray();
        var entitlements = await db.Entitlements.AsNoTracking().Where(e => ids.Contains(e.UserId) && products.Contains(e.ProductId))
            .Select(e => new { e.UserId, e.ProductId }).ToListAsync(token);
        var followed = await db.OfficialFollows.Where(f => f.UserId == caller && ids.Contains(f.FollowedUserId)).Select(f => f.FollowedUserId).ToListAsync(token);
        var allowedPresence = (await policy.ConnectionsAsync(caller, SocialAction.SeePresence, token)).Select(c => c.UserId).ToHashSet();
        var allowedStats = (await policy.ConnectionsAsync(caller, SocialAction.SeeStatistics, token)).Select(c => c.UserId).ToHashSet();
        var invites = (await policy.ConnectionsAsync(caller, SocialAction.Invite, token)).Select(c => c.UserId).ToHashSet();
        var challenges = (await policy.ConnectionsAsync(caller, SocialAction.Challenge, token)).Select(c => c.UserId).ToHashSet();
        var presenceIds = ids.Where(id => id == caller || allowedPresence.Contains(id)
            || officials.GetValueOrDefault(id)?.Discoverable == true && (privacy.GetValueOrDefault(id)?.Presence ?? SocialVisibility.Connections) == SocialVisibility.Connections).ToArray();
        var states = await presence.GetAsync(presenceIds, token);
        var statsIds = ids.Where(id => id == caller || allowedStats.Contains(id)).ToArray();
        var aced = await db.UserLessonProgress.AsNoTracking().Where(p => statsIds.Contains(p.UserId) && p.CompletionState == CompletionState.Aced)
            .GroupBy(p => p.UserId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(p => p.Id, p => p.Count, token);
        var matches = await db.MatchPlacements.AsNoTracking().Where(p => statsIds.Contains(p.UserId) && !p.Forfeited && !p.Flagged)
            .GroupBy(p => p.UserId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(p => p.Id, p => p.Count, token);
        var streaks = await db.UserStreaks.AsNoTracking().Where(p => statsIds.Contains(p.UserId) && p.StreakKey == "daily").ToDictionaryAsync(p => p.UserId, p => p.Best, token);
        var now = DateTime.UtcNow;
        return ids.Select(id =>
        {
            var identity = officials.GetValueOrDefault(id); var selected = selections.GetValueOrDefault(id);
            var equipment = outfits.Where(e => e.UserId == id).ToArray();
            return new GamingProfileDto(id, identity is null ? handles.GetValueOrDefault(id) : arabic ? identity.DisplayNameAr : identity.DisplayNameEn,
                id == caller, identity?.Kind, identity?.Verified ?? false, identity is null ? null : arabic ? identity.TitleAr : identity.TitleEn,
                states.TryGetValue(id, out var state) ? state : null,
                statsIds.Contains(id) ? new(aced.GetValueOrDefault(id), matches.GetValueOrDefault(id), streaks.GetValueOrDefault(id)) : null,
                new(equipment.FirstOrDefault()?.BodyType.ToString() ?? "Male", equipment.Where(e => e.SlotKey != null && e.CosmeticKey != null)
                    .Select(e => new SafeEquipmentSlot(e.SlotKey!, e.CosmeticKey!, e.ColorKey)).ToArray()),
                Selection(selected?.SelectionJson ?? "[]").Where(content.ContainsKey)
                    .Where(k => (!content[k].OfficialOnly || identity != null) && (content[k].ProductId == null
                        || entitlements.Any(e => e.UserId == id && e.ProductId == content[k].ProductId)))
                    .Select(k => ContentDto(content[k])).ToArray(),
                selected?.Version ?? 0, invites.Contains(id), challenges.Contains(id), id != caller && identity?.Discoverable == true,
                followed.Contains(id), now);
        }).ToList();
    }

    public async Task<ServiceResult<IReadOnlyList<ShowcaseContentDto>>> ContentAsync(Guid user, CancellationToken token = default)
    {
        var official = await db.OfficialProfiles.AnyAsync(p => p.UserId == user, token);
        var content = await db.ShowcaseContents.AsNoTracking().Where(c => c.Enabled && (!c.OfficialOnly || official)).OrderBy(c => c.Key).Take(500).ToListAsync(token);
        return ServiceResult<IReadOnlyList<ShowcaseContentDto>>.Success(content.Select(ContentDto).ToArray());
    }

    public async Task<ServiceResult<GamingProfileDto>> SelectAsync(Guid user, ShowcaseSelectionRequest request, CancellationToken token = default)
    {
        if (request.Keys is null || request.Keys.Count > 12 || request.Keys.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 80)
            || request.Keys.Distinct().Count() != request.Keys.Count) return InvalidContent();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await LockUserAsync(db, user, token);
        var row = await db.PlayerShowcases.FirstOrDefaultAsync(p => p.UserId == user, token);
        if ((row?.Version ?? 0) != request.ExpectedVersion)
            return ServiceResult<GamingProfileDto>.Failure(SocialPlatformErrors.Stale, ServiceErrorKind.Conflict, "Refresh the showcase.");
        var keys = request.Keys.ToArray();
        var selected = await db.ShowcaseContents.AsNoTracking().Where(c => keys.Contains(c.Key) && c.Enabled).ToListAsync(token);
        var official = await db.OfficialProfiles.AnyAsync(p => p.UserId == user, token);
        var productIds = selected.Where(c => c.ProductId != null).Select(c => c.ProductId!.Value).ToArray();
        var owned = await db.Entitlements.Where(e => e.UserId == user && productIds.Contains(e.ProductId)).Select(e => e.ProductId).ToListAsync(token);
        if (selected.Count != keys.Length || selected.Any(c => c.OfficialOnly && !official || c.ProductId is { } product && !owned.Contains(product))
            || selected.GroupBy(c => c.Kind).Any(g => g.Count() > (g.Key is ShowcaseContentKind.Prop or ShowcaseContentKind.Effect ? 3 : 1))) return InvalidContent();
        if (row is null) { row = new PlayerShowcase { UserId = user }; db.PlayerShowcases.Add(row); }
        row.SelectionJson = JsonSerializer.Serialize(keys); row.Version++; row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return await ReadAsync(user, user, token);
    }

    public async Task<ServiceResult> FollowAsync(Guid user, Guid official, bool follow, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await LockUserAsync(db, user, token);
        var row = await db.OfficialFollows.FirstOrDefaultAsync(f => f.UserId == user && f.FollowedUserId == official, token);
        if (!follow) { if (row != null) db.OfficialFollows.Remove(row); }
        else
        {
            if (user == official || !await db.OfficialProfiles.AnyAsync(p => p.UserId == official && p.Discoverable, token)
                || await blocks.IsBlockedEitherWayAsync(user, official, token)) return ServiceResult.Failure(SocialPlatformErrors.Unavailable, ServiceErrorKind.NotFound, "Unavailable.");
            var now = DateTime.UtcNow;
            if (await db.SocialRestrictions.AnyAsync(r => r.UserId == user && r.RevokedAtUtc == null && r.StartsAtUtc <= now && (r.ExpiresAtUtc == null || r.ExpiresAtUtc > now), token))
                return ServiceResult.Failure(SocialPlatformErrors.Restricted, ServiceErrorKind.Forbidden, "Social actions restricted.");
            if (row == null)
            {
                if (await db.OfficialFollows.CountAsync(f => f.UserId == user, token) >= 200) return ServiceResult.Invalid("Follow limit reached.");
                db.OfficialFollows.Add(new() { UserId = user, FollowedUserId = official, CreatedAtUtc = now });
            }
        }
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }

    public async Task<ServiceResult<CursorPage<OfficialActivityDto>>> ActivityAsync(Guid user, long before, CancellationToken token = default)
    {
        if (before < 0) return ServiceResult<CursorPage<OfficialActivityDto>>.Invalid("Invalid cursor.");
        var blocked = (await blocks.BlockedEitherWayAsync(user, token)).ToArray(); var now = DateTime.UtcNow;
        var rows = await db.OfficialActivities.AsNoTracking().Where(a => (before == 0 || a.Sequence < before)
            && a.StartsAtUtc <= now && a.ExpiresAtUtc > now && !blocked.Contains(a.UserId)
            && db.OfficialProfiles.Any(p => p.UserId == a.UserId && p.Discoverable)
            && db.OfficialFollows.Any(f => f.UserId == user && f.FollowedUserId == a.UserId)
            && !db.PlayerMutes.Any(m => m.UserId == user && m.MutedUserId == a.UserId)
            && (a.EventId == null || db.PlayEvents.Any(e => e.Id == a.EventId && e.IsActive && e.CancelledAtUtc == null)))
            .OrderByDescending(a => a.Sequence).Take(51).ToListAsync(token);
        var arabic = await language.ResolveForUserAsync(user, token) == LanguageIds.Arabic;
        return ServiceResult<CursorPage<OfficialActivityDto>>.Success(new(rows.Take(50).Select(a => new OfficialActivityDto(a.Sequence, a.UserId,
            arabic ? a.TitleAr : a.TitleEn, a.EventId, Utc(a.StartsAtUtc), Utc(a.ExpiresAtUtc))).ToArray(), rows.Count > 50 ? rows[49].Sequence : null, now));
    }

    internal static Task<int> LockUserAsync(ApplicationDbContext db, Guid user, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {0}", [user], token);
    internal static ShowcaseContentDto ContentDto(ShowcaseContent c) => new(c.Key, c.Kind, c.AssetKey, c.FallbackKey, c.ProductId, c.OfficialOnly, c.Enabled, c.MinimumQuality, c.ContractVersion);
    internal static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    internal static string[] Selection(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    private static ServiceResult<GamingProfileDto> Unavailable() => ServiceResult<GamingProfileDto>.Failure(SocialPlatformErrors.Unavailable, ServiceErrorKind.NotFound, "Unavailable.");
    private static ServiceResult<GamingProfileDto> InvalidContent() => ServiceResult<GamingProfileDto>.Failure(SocialPlatformErrors.ContentInvalid, ServiceErrorKind.Validation, "Select owned approved content.");
}
