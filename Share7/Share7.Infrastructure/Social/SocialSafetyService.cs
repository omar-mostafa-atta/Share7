using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Feed;
using Share7.Application.Social;
using Share7.Domain.Social;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Social;

public sealed class SocialSafetyService(ApplicationDbContext db, IGamingProfileService profiles,
    IAuditLog audit, IPlayerEventPublisher events) : ISocialSafetyService
{
    public async Task<PrivacyDto> PrivacyAsync(Guid user, CancellationToken token = default) =>
        Privacy(await db.SocialPrivacy.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user, token) ?? new());

    public async Task<ServiceResult<PrivacyDto>> SetPrivacyAsync(Guid user, PrivacyDto request, CancellationToken token = default)
    {
        if (new[] { request.Profile, request.Presence, request.Statistics, request.Invitations, request.Challenges }.Any(v => !Enum.IsDefined(v)))
            return ServiceResult<PrivacyDto>.Invalid("Invalid visibility.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        var row = await db.SocialPrivacy.FirstOrDefaultAsync(p => p.UserId == user, token);
        if (row is null) { row = new() { UserId = user }; db.SocialPrivacy.Add(row); }
        row.Profile = request.Profile; row.Presence = request.Presence; row.Statistics = request.Statistics;
        row.Invitations = request.Invitations; row.Challenges = request.Challenges; row.FriendRequests = request.FriendRequests; row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult<PrivacyDto>.Success(Privacy(row));
    }
    private static PrivacyDto Privacy(SocialPrivacy p) => new(p.Profile, p.Presence, p.Statistics, p.Invitations, p.Challenges, p.FriendRequests);

    public async Task<ServiceResult> MuteAsync(Guid user, Guid target, bool mute, CancellationToken token = default)
    {
        if (user == target) return ServiceResult.Invalid("Cannot mute yourself.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        var row = await db.PlayerMutes.FirstOrDefaultAsync(m => m.UserId == user && m.MutedUserId == target, token);
        if (!mute && row != null) db.PlayerMutes.Remove(row);
        if (mute && row == null && await db.Users.AnyAsync(u => u.Id == target, token)) db.PlayerMutes.Add(new() { UserId = user, MutedUserId = target, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }

    public async Task<ServiceResult<ReportReceipt>> ReportAsync(Guid user, ReportRequest request, CancellationToken token = default)
    {
        if (user == request.UserId || !Enum.IsDefined(request.Reason) || !SocialProfileAdminService.Key(request.RequestId, 64))
            return ServiceResult<ReportReceipt>.Invalid("Choose a reason and a bounded request key.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await GamingProfileService.LockUserAsync(db, user, token);
        var existing = await db.PlayerReports.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == user && r.RequestId == request.RequestId, token);
        if (existing != null)
            return existing.ReportedUserId == request.UserId && existing.SessionId == request.SessionId && existing.Reason == request.Reason
                ? ServiceResult<ReportReceipt>.Success(Receipt(existing)) : ServiceResult<ReportReceipt>.Conflict("Report key already used.");
        object evidence;
        if (request.SessionId is { } session)
        {
            if (!await db.MultiplayerSessionPlayers.AnyAsync(p => p.SessionId == session && p.UserId == user, token)
                || !await db.MultiplayerSessionPlayers.AnyAsync(p => p.SessionId == session && p.UserId == request.UserId, token)) return Unavailable();
            var room = await db.MultiplayerSessions.AsNoTracking().Where(s => s.Id == session)
                .Select(s => new { s.Id, s.State, s.GameId, s.ModeId, s.StartedAtUtc, s.EndedAtUtc }).FirstOrDefaultAsync(token);
            if (room is null) return Unavailable();
            evidence = new { context = "session", room, capturedAtUtc = DateTime.UtcNow };
        }
        else
        {
            if (!(await profiles.ReadAsync(user, request.UserId, token)).Succeeded) return Unavailable();
            var approved = await db.OfficialProfiles.AsNoTracking().Where(p => p.UserId == request.UserId)
                .Select(p => new { p.Kind, p.Verified }).FirstOrDefaultAsync(token);
            evidence = new { context = "profile", approved, capturedAtUtc = DateTime.UtcNow };
        }
        var now = DateTime.UtcNow;
        if (await db.PlayerReports.CountAsync(r => r.UserId == user && r.CreatedAtUtc >= now.Date, token) >= 10)
            return ServiceResult<ReportReceipt>.Failure(SocialPlatformErrors.ReportLimit, ServiceErrorKind.Conflict, "Daily report limit.");
        var report = new PlayerReport { Id = Guid.NewGuid(), UserId = user, ReportedUserId = request.UserId, SessionId = request.SessionId,
            Reason = request.Reason, RequestId = request.RequestId, EvidenceJson = JsonSerializer.Serialize(evidence), CreatedAtUtc = now };
        db.PlayerReports.Add(report); await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return ServiceResult<ReportReceipt>.Success(Receipt(report));
    }
    private static ReportReceipt Receipt(PlayerReport r) => new(r.Id, r.State, GamingProfileService.Utc(r.CreatedAtUtc));
    private static ServiceResult<ReportReceipt> Unavailable() => ServiceResult<ReportReceipt>.Failure(SocialPlatformErrors.Unavailable, ServiceErrorKind.NotFound, "Context unavailable.");

    public async Task<CursorPage<ModerationCaseDto>> CasesAsync(long after, ModerationState? state, CancellationToken token = default)
    {
        var rows = await db.PlayerReports.AsNoTracking().Where(r => r.Sequence > after && (state == null || r.State == state))
            .OrderBy(r => r.Sequence).Take(51).ToListAsync(token);
        return new(rows.Take(50).Select(r => new ModerationCaseDto(r.Id, r.Sequence, r.UserId, r.ReportedUserId, r.SessionId,
            r.Reason, r.EvidenceJson, r.State, r.DecisionCode, GamingProfileService.Utc(r.CreatedAtUtc))).ToArray(), rows.Count > 50 ? rows[49].Sequence : null, DateTime.UtcNow);
    }

    public async Task<ServiceResult> DecideAsync(Guid report, ModerationDecisionRequest request, CancellationToken token = default)
    {
        if (request.State is not (ModerationState.Dismissed or ModerationState.Actioned) || !SocialProfileAdminService.Key(request.DecisionCode, 40)
            || request.RestrictDays is < 1 or > 365 || request.PermanentRestriction && request.RestrictDays != null
            || request.State == ModerationState.Dismissed && (request.RestrictDays != null || request.PermanentRestriction))
            return ServiceResult.Invalid("Choose a recorded decision and bounded restriction.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SELECT [Id] FROM [PlayerReports] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {0}", [report], token);
        var row = await db.PlayerReports.FirstOrDefaultAsync(r => r.Id == report, token);
        if (row is null) return ServiceResult.NotFound("Report unavailable.");
        if (row.State != ModerationState.Open)
            return row.State == request.State && row.DecisionCode == request.DecisionCode && row.RestrictDays == request.RestrictDays
                && row.PermanentRestriction == request.PermanentRestriction ? ServiceResult.Success() : ServiceResult.Conflict("Already decided.");
        row.State = request.State; row.DecisionCode = request.DecisionCode; row.DecidedAtUtc = DateTime.UtcNow;
        row.RestrictDays = request.RestrictDays; row.PermanentRestriction = request.PermanentRestriction;
        if (request.RestrictDays != null || request.PermanentRestriction)
        {
            var restriction = new SocialRestriction { Id = Guid.NewGuid(), UserId = row.ReportedUserId, ReportId = row.Id,
                StartsAtUtc = row.DecidedAtUtc.Value, ExpiresAtUtc = request.PermanentRestriction ? null : row.DecidedAtUtc.Value.AddDays(request.RestrictDays!.Value) };
            db.SocialRestrictions.Add(restriction);
            events.Stage(row.ReportedUserId, "social.restriction.created", new { restrictionId = restriction.Id, restriction.ExpiresAtUtc });
        }
        audit.Record(new("social.report.decided", "social", "Decided a moderation case.", "report", row.Id.ToString(),
            new { row.State, row.DecisionCode, request.RestrictDays, request.PermanentRestriction }));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }

    public async Task<IReadOnlyList<RestrictionDto>> RestrictionsAsync(Guid user, CancellationToken token = default)
    {
        var now = DateTime.UtcNow;
        return await db.SocialRestrictions.AsNoTracking().Where(r => r.UserId == user && r.RevokedAtUtc == null
            && (r.ExpiresAtUtc == null || r.ExpiresAtUtc > now)).OrderByDescending(r => r.StartsAtUtc).Take(50)
            .Select(r => new RestrictionDto(r.Id, r.StartsAtUtc, r.ExpiresAtUtc, r.AppealedAtUtc != null, r.AppealResolutionCode)).ToListAsync(token);
    }
    public async Task<ServiceResult> AppealAsync(Guid user, Guid restriction, string reasonCode, CancellationToken token = default)
    {
        if (reasonCode is not ("mistake" or "context" or "resolved")) return ServiceResult.Invalid("Choose an appeal reason.");
        var changed = await db.SocialRestrictions.Where(r => r.Id == restriction && r.UserId == user && r.AppealedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.AppealCode, reasonCode).SetProperty(r => r.AppealedAtUtc, DateTime.UtcNow), token);
        return changed > 0 || await db.SocialRestrictions.AnyAsync(r => r.Id == restriction && r.UserId == user, token)
            ? ServiceResult.Success() : ServiceResult.NotFound("Restriction unavailable.");
    }
    public async Task<ServiceResult> RevokeAsync(Guid restriction, string reasonCode, CancellationToken token = default)
    {
        if (!SocialProfileAdminService.Key(reasonCode, 40)) return ServiceResult.Invalid("Provide a recorded reason.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [SocialRestrictions] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {restriction}", token);
        var row = await db.SocialRestrictions.FirstOrDefaultAsync(r => r.Id == restriction, token);
        if (row is null) return ServiceResult.NotFound("Restriction unavailable.");
        if (row.RevokedAtUtc != null) return ServiceResult.Success();
        row.RevokedAtUtc = DateTime.UtcNow;
        if (row.AppealedAtUtc != null) { row.AppealReviewedAtUtc = row.RevokedAtUtc; row.AppealResolutionCode = "revoked:" + reasonCode; }
        events.Stage(row.UserId, "social.restriction.revoked", new { restrictionId = row.Id });
        audit.Record(new("social.restriction.revoked", "social", "Revoked a social restriction.", "restriction", row.Id.ToString(), new { reasonCode }));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }
    public async Task<IReadOnlyList<ModerationAppealDto>> AppealsAsync(CancellationToken token = default) =>
        await db.SocialRestrictions.AsNoTracking().Where(r => r.AppealedAtUtc != null && r.AppealReviewedAtUtc == null && r.RevokedAtUtc == null
            && (r.ExpiresAtUtc == null || r.ExpiresAtUtc > DateTime.UtcNow)).OrderBy(r => r.AppealedAtUtc).Take(100)
            .Select(r => new ModerationAppealDto(r.Id, r.UserId, r.ReportId, r.AppealCode!, r.AppealedAtUtc!.Value, r.ExpiresAtUtc)).ToListAsync(token);

    public async Task<ServiceResult> ReviewAppealAsync(Guid restriction, bool revoke, string reasonCode, CancellationToken token = default)
    {
        if (!SocialProfileAdminService.Key(reasonCode, 40)) return ServiceResult.Invalid("Provide a recorded appeal decision.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [SocialRestrictions] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {restriction}", token);
        var row = await db.SocialRestrictions.FirstOrDefaultAsync(r => r.Id == restriction, token);
        if (row?.AppealedAtUtc == null) return ServiceResult.NotFound("Appeal unavailable.");
        var resolution = (revoke ? "revoked:" : "upheld:") + reasonCode;
        if (row.AppealReviewedAtUtc != null) return row.AppealResolutionCode == resolution ? ServiceResult.Success() : ServiceResult.Conflict("Appeal already reviewed.");
        row.AppealReviewedAtUtc = DateTime.UtcNow; row.AppealResolutionCode = resolution;
        if (revoke) row.RevokedAtUtc = row.AppealReviewedAtUtc;
        events.Stage(row.UserId, "social.restriction.appeal_reviewed", new { restrictionId = row.Id, outcome = revoke ? "revoked" : "upheld" });
        audit.Record(new("social.appeal.reviewed", "social", "Reviewed a player appeal.", "restriction", row.Id.ToString(), new { revoke, reasonCode }));
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return ServiceResult.Success();
    }
}
