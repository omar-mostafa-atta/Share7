using Microsoft.EntityFrameworkCore;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

public sealed class SessionArchiveService(ApplicationDbContext db, IAuditLog audit) : ISessionArchiveService
{
    public const int FullSessionDays = 90;
    public const int SummaryDays = 365;
    public const int BatchSize = 100;

    public async Task<int> SweepAsync(CancellationToken token = default)
    {
        var now = DateTime.UtcNow; var cutoff = now.AddDays(-FullSessionDays);
        await db.SessionArchives.Where(a => a.ExpiresAtUtc <= now).OrderBy(a => a.ExpiresAtUtc).Take(BatchSize).ExecuteDeleteAsync(token);
        var terminal = MultiplayerSessionStates.Terminal.ToArray();
        var ids = await db.MultiplayerSessions.AsNoTracking().Where(s => terminal.Contains(s.State) && s.EndedAtUtc < cutoff)
            .OrderBy(s => s.EndedAtUtc).Select(s => s.Id).Take(BatchSize).ToListAsync(token);
        var archived = 0;
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [MultiplayerSessions] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {id}", token);
            var session = await db.MultiplayerSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && terminal.Contains(s.State) && s.EndedAtUtc < cutoff, token);
            if (session is null) continue;
            // End + 90 days + one year: a delayed worker must not extend the retention policy.
            var expiry = session.EndedAtUtc!.Value.AddDays(FullSessionDays + SummaryDays);
            if (expiry > now)
            {
                var participants = await db.MultiplayerSessionPlayers.AsNoTracking().Where(p => p.SessionId == id
                    && !db.MultiplayerSessionBans.Any(b => b.SessionId == id && b.UserId == p.UserId)).Select(p => p.UserId).Distinct().ToListAsync(token);
                var placements = await db.MatchPlacements.AsNoTracking().Where(p => p.SessionId == id).ToDictionaryAsync(p => p.UserId, token);
                db.SessionArchives.Add(new() { Id = id, GameId = session.GameId, ModeId = session.ModeId,
                    StartedAtUtc = session.StartedAtUtc, EndedAtUtc = session.EndedAtUtc.Value, ArchivedAtUtc = now,
                    ExpiresAtUtc = expiry, State = session.State, ParticipantCount = participants.Count });
                foreach (var user in participants)
                {
                    placements.TryGetValue(user, out var place);
                    db.SessionArchiveParticipants.Add(new() { SessionId = id, UserId = user, Placement = place?.Placement,
                        IsWinner = place?.IsWinner ?? false, Forfeited = place?.Forfeited ?? false });
                }
                await db.SaveChangesAsync(token);
            }
            // Session-owned seating/invitations/lesson payload cascade. Results, ratings, rewards and
            // pending physical-prize claims have independent lifetimes and are deliberately untouched.
            archived += await db.MultiplayerSessions.Where(s => s.Id == id).ExecuteDeleteAsync(token);
            await transaction.CommitAsync(token); db.ChangeTracker.Clear();
        }
        return archived;
    }

    public async Task<ServiceResult<SessionArchiveDto>> ReadAsync(Guid user, Guid session, bool isAdmin = false, CancellationToken token = default)
    {
        var now = DateTime.UtcNow;
        var archive = await db.SessionArchives.AsNoTracking().FirstOrDefaultAsync(a => a.Id == session && a.ExpiresAtUtc > now, token);
        if (archive is null) return ServiceResult<SessionArchiveDto>.NotFound("History unavailable.");
        var participant = await db.SessionArchiveParticipants.AsNoTracking().FirstOrDefaultAsync(p => p.SessionId == session && p.UserId == user, token);
        if (participant is null && !isAdmin) return ServiceResult<SessionArchiveDto>.NotFound("History unavailable.");
        if (isAdmin)
        {
            audit.Record(new("multiplayer.archive.read", "multiplayer", "Read a minimal archived session.", "session", session.ToString()));
            await db.SaveChangesAsync(token);
        }
        static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return ServiceResult<SessionArchiveDto>.Success(new(session, archive.GameId, archive.ModeId,
            archive.StartedAtUtc is { } start ? Utc(start) : null, Utc(archive.EndedAtUtc), Utc(archive.ExpiresAtUtc), archive.State,
            archive.ParticipantCount, participant?.Placement, participant?.IsWinner ?? false, participant?.Forfeited ?? false));
    }
}
