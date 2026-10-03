using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Social;
using Share7.Domain.Social;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Social;

public sealed class SocialProfileAdminService(ApplicationDbContext db, IAuditLog audit) : ISocialProfileAdminService
{
    internal static bool Key(string? text, int max = 80) => text is { Length: > 0 } && text.Length <= max
        && Regex.IsMatch(text, "^[a-zA-Z0-9_.:-]+$", RegexOptions.CultureInvariant);
    internal static bool Text(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max
        && !text.Any(c => char.IsControl(c) || c is '<' or '>');

    public async Task<ServiceResult> SetIdentityAsync(Guid user, OfficialProfileInput request, CancellationToken token = default)
    {
        if (!Enum.IsDefined(request.Kind) || !Text(request.DisplayNameEn, 80) || !Text(request.DisplayNameAr, 80)
            || !Text(request.TitleEn, 80) || !Text(request.TitleAr, 80)) return ServiceResult.Invalid("Provide a valid bilingual official identity.");
        // Official discovery is curated adult/team identity, never an opt-in public child profile.
        if (!await db.StudentProfiles.AnyAsync(p => p.UserId == user && p.Age >= 18, token)
            && !await db.StaffProfiles.AnyAsync(p => p.UserId == user, token)) return ServiceResult.Invalid("Approve an adult or staff account.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        var row = await db.OfficialProfiles.FirstOrDefaultAsync(p => p.UserId == user, token);
        if (row is null) { row = new OfficialProfile { UserId = user }; db.OfficialProfiles.Add(row); }
        row.Kind = request.Kind; row.Verified = request.Verified; row.Discoverable = request.Discoverable;
        row.DisplayNameEn = request.DisplayNameEn.Trim(); row.DisplayNameAr = request.DisplayNameAr.Trim();
        row.TitleEn = request.TitleEn.Trim(); row.TitleAr = request.TitleAr.Trim(); row.UpdatedAtUtc = DateTime.UtcNow;
        audit.Record(new("social.identity.updated", "social", "Updated approved public presentation.", "user", user.ToString(),
            new { request.Kind, request.Verified, request.Discoverable }));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetContentAsync(ShowcaseContentDto request, CancellationToken token = default)
    {
        if (!Key(request.Key) || !Key(request.AssetKey, 160) || !Enum.IsDefined(request.Kind) || request.MinimumQuality is < 0 or > 2
            || request.ContractVersion != 1 || request.FallbackKey == request.Key) return ServiceResult.Invalid("Invalid content contract.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = N'Social.Showcase.Authoring', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @result < 0 THROW 51000, 'Content authoring lock unavailable', 1;", token);
        if (request.ProductId is { } product && !await db.Products.AnyAsync(p => p.Id == product, token)) return ServiceResult.Invalid("Unknown product.");
        if (request.FallbackKey is { } fallback && !await db.ShowcaseContents.AnyAsync(c => c.Key == fallback
            && c.Kind == request.Kind && c.Enabled && c.MinimumQuality == 0 && c.ProductId == null && !c.OfficialOnly && c.FallbackKey == null, token))
            return ServiceResult.Invalid("Fallback must be a free, enabled base-quality definition of the same kind.");
        var row = await db.ShowcaseContents.FirstOrDefaultAsync(c => c.Key == request.Key, token);
        if (row is not null && (row.Kind != request.Kind || row.AssetKey != request.AssetKey || row.FallbackKey != request.FallbackKey
            || row.ProductId != request.ProductId || row.OfficialOnly != request.OfficialOnly || row.MinimumQuality != request.MinimumQuality))
            return ServiceResult.Conflict("Version content under a new key; existing contracts are immutable.");
        if (row is null)
        {
            if (await db.ShowcaseContents.CountAsync(token) >= 500) return ServiceResult.Invalid("The active content contract is bounded to 500 definitions.");
            row = new ShowcaseContent { Key = request.Key, Kind = request.Kind, AssetKey = request.AssetKey,
                FallbackKey = request.FallbackKey, ProductId = request.ProductId, OfficialOnly = request.OfficialOnly,
                MinimumQuality = request.MinimumQuality, ContractVersion = 1 };
            db.ShowcaseContents.Add(row);
        }
        row.Enabled = request.Enabled;
        audit.Record(new("social.content.updated", "social", "Updated showcase availability.", "showcase", row.Key, new { row.Kind, row.Enabled }));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }

    public async Task<IReadOnlyList<ShowcaseContentDto>> ContentAsync(CancellationToken token = default) =>
        (await db.ShowcaseContents.AsNoTracking().OrderBy(c => c.Key).Take(500).ToListAsync(token)).Select(GamingProfileService.ContentDto).ToArray();

    public async Task<ServiceResult> PublishActivityAsync(Guid user, OfficialActivityInput request, CancellationToken token = default)
    {
        var start = GamingProfileService.Utc(request.StartsAtUtc); var end = GamingProfileService.Utc(request.ExpiresAtUtc);
        if (!Key(request.PublicationKey, 64) || !Text(request.TitleEn, 160) || !Text(request.TitleAr, 160)
            || end <= DateTime.UtcNow || end <= start || end - start > TimeSpan.FromDays(30)) return ServiceResult.Invalid("Invalid bilingual activity/window.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        if (!await db.OfficialProfiles.AnyAsync(p => p.UserId == user && p.Discoverable, token)) return ServiceResult.NotFound("Unavailable identity.");
        if (request.EventId is { } id && !await db.PlayEvents.AnyAsync(e => e.Id == id && e.IsActive && e.CancelledAtUtc == null, token))
            return ServiceResult.Invalid("Reference a real available event.");
        var existing = await db.OfficialActivities.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == user && a.PublicationKey == request.PublicationKey, token);
        if (existing != null)
            return existing.TitleEn == request.TitleEn.Trim() && existing.TitleAr == request.TitleAr.Trim() && existing.EventId == request.EventId
                && existing.StartsAtUtc == start && existing.ExpiresAtUtc == end ? ServiceResult.Success() : ServiceResult.Conflict("Publication key already used.");
        if (await db.OfficialActivities.CountAsync(a => a.UserId == user && a.ExpiresAtUtc > DateTime.UtcNow, token) >= 20)
            return ServiceResult.Invalid("Too many active publications.");
        db.OfficialActivities.Add(new() { UserId = user, PublicationKey = request.PublicationKey, TitleEn = request.TitleEn.Trim(),
            TitleAr = request.TitleAr.Trim(), EventId = request.EventId, StartsAtUtc = start, ExpiresAtUtc = end });
        audit.Record(new("social.activity.published", "social", "Published an official activity.", "user", user.ToString(), new { request.EventId, start, end }));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }
}
