using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Social;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Social;

public sealed class InboxService(ApplicationDbContext db) : IInboxService
{
    public static readonly string[] Categories = ["friend", "challenge", "multiplayer", "reward", "brainpass", "safety", "system"];
    private static string Category(string type) => type.StartsWith("multiplayer.friend.", StringComparison.Ordinal) || type.StartsWith("social.team.", StringComparison.Ordinal) ? "friend"
        : type.StartsWith("multiplayer.challenge.", StringComparison.Ordinal) ? "challenge"
        : type.StartsWith("brainpass.", StringComparison.Ordinal) ? "brainpass"
        : type.StartsWith("social.restriction.", StringComparison.Ordinal) ? "safety"
        : type.StartsWith("multiplayer.", StringComparison.Ordinal) ? "multiplayer"
        : type.StartsWith("reward.", StringComparison.Ordinal) ? "reward" : "system";

    public async Task<ServiceResult<CursorPage<InboxItemDto>>> ReadAsync(Guid user, long before, CancellationToken token = default)
    {
        if (before < 0) return ServiceResult<CursorPage<InboxItemDto>>.Invalid("Invalid cursor.");
        var now = DateTime.UtcNow;
        var rows = await db.PlayerEvents.AsNoTracking().Where(e => e.RecipientUserId == user && e.ExpiresAtUtc > now && (before == 0 || e.Sequence < before))
            .OrderByDescending(e => e.Sequence).Take(101).ToListAsync(token);
        var ids = rows.Select(e => e.EventId).ToArray();
        var read = await db.InboxReads.Where(r => r.UserId == user && ids.Contains(r.EventId)).Select(r => r.EventId).ToListAsync(token);
        var disabled = await db.InboxPreferences.Where(p => p.UserId == user && !p.Enabled).Select(p => p.Category).ToListAsync(token);
        var muted = await db.PlayerMutes.Where(m => m.UserId == user).Select(m => m.MutedUserId).ToListAsync(token);
        var blocked = await db.PlayerBlocks.Where(m => m.UserId == user || m.BlockedUserId == user)
            .Select(m => m.UserId == user ? m.BlockedUserId : m.UserId).ToListAsync(token);
        var hidden = muted.Concat(blocked).ToHashSet();
        var items = new List<InboxItemDto>();
        foreach (var row in rows.Take(100))
        {
            var category = Category(row.Type);
            if (category is not ("safety" or "reward") && disabled.Contains(category)) continue;
            using var document = JsonDocument.Parse(row.PayloadJson);
            var payload = document.RootElement;
            if (payload.ValueKind == JsonValueKind.Object && new[] { "fromUserId", "senderUserId", "challengerUserId", "leaderUserId", "inviterUserId" }
                .Any(key => payload.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                    && value.TryGetGuid(out var actor) && hidden.Contains(actor))) continue;
            items.Add(new(row.Sequence, row.EventId, category, "inbox." + row.Type, row.Type, SafePayload(payload), read.Contains(row.EventId),
                GamingProfileService.Utc(row.OccurredAtUtc), GamingProfileService.Utc(row.ExpiresAtUtc)));
        }
        // The cursor advances across filtered rows too, so preferences cannot trap pagination.
        return ServiceResult<CursorPage<InboxItemDto>>.Success(new(items, rows.Count > 100 ? rows[99].Sequence : null, now));
    }

    // Legacy feed events may contain configured names or free text. The social inbox carries
    // only navigation identifiers and bounded machine values; copy comes from client templates.
    private static JsonElement SafePayload(JsonElement payload)
    {
        var safe = new Dictionary<string, object?>();
        if (payload.ValueKind != JsonValueKind.Object) return JsonSerializer.SerializeToElement(safe);
        foreach (var property in payload.EnumerateObject())
        {
            var key = property.Name;
            var value = property.Value;
            if (key is "requestId" or "eventId" or "sessionId" or "partyId" or "teamId" or "tournamentId" or "matchId"
                or "fromUserId" or "senderUserId" or "challengerUserId" or "leaderUserId" or "inviterUserId"
                or "opponentUserId" or "userId" or "gameId" or "modeId" or "seasonId" or "restrictionId" or "transactionId" or "awardId")
            {
                if (value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id)) safe[key] = id;
            }
            else if (key is "tier" or "placement" or "rank" or "round" or "gameNumber")
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number is >= 0 and <= 1000000) safe[key] = number;
            }
            else if (key is "state" or "reason" or "reasonCode" or "outcome" or "track" or "type" or "objectiveKey" or "cycleKey")
            {
                if (value.ValueKind == JsonValueKind.String && SocialProfileAdminService.Key(value.GetString()!, 64)) safe[key] = value.GetString();
                else if (key == "track" && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var track) && track is 0 or 1) safe[key] = track;
            }
            else if (key is "deadlineAtUtc" or "startsAtUtc" or "expiresAtUtc" or "ExpiresAtUtc")
            {
                if (value.ValueKind == JsonValueKind.String && value.TryGetDateTime(out var at)) safe[key] = GamingProfileService.Utc(at);
            }
        }
        return JsonSerializer.SerializeToElement(safe);
    }

    public async Task<ServiceResult> MarkReadAsync(Guid user, Guid eventId, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        if (!await db.PlayerEvents.AnyAsync(e => e.EventId == eventId && e.RecipientUserId == user, token)) return ServiceResult.NotFound("Unavailable notification.");
        if (!await db.InboxReads.AnyAsync(r => r.UserId == user && r.EventId == eventId, token))
            db.InboxReads.Add(new() { UserId = user, EventId = eventId, ReadAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }
    public async Task<IReadOnlyDictionary<string, bool>> PreferencesAsync(Guid user, CancellationToken token = default)
    {
        var saved = await db.InboxPreferences.AsNoTracking().Where(p => p.UserId == user).ToDictionaryAsync(p => p.Category, p => p.Enabled, token);
        return Categories.ToDictionary(c => c, c => c is "safety" or "reward" || saved.GetValueOrDefault(c, true));
    }
    public async Task<ServiceResult> PreferenceAsync(Guid user, string category, bool enabled, CancellationToken token = default)
    {
        if (!Categories.Contains(category) || !enabled && category is "safety" or "reward") return ServiceResult.Invalid("Required safety/reward notifications remain available.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        var row = await db.InboxPreferences.FirstOrDefaultAsync(p => p.UserId == user && p.Category == category, token);
        if (row is null) { row = new() { UserId = user, Category = category }; db.InboxPreferences.Add(row); }
        row.Enabled = enabled; await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }
}
