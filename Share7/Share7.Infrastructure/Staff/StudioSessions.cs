using Microsoft.EntityFrameworkCore;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Staff;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// Opening and ending Studio sessions, and the sign-in history — the pieces the sign-in service,
/// the member's own account page and Team &amp; Access all need, written once.
/// </summary>
public class StudioSessions
{
    private readonly ApplicationDbContext _db;
    private readonly StudioTokenIssuer _tokens;
    private readonly IStudioSessionValidator _validator;

    public StudioSessions(ApplicationDbContext db, StudioTokenIssuer tokens, IStudioSessionValidator validator)
    {
        _db = db;
        _tokens = tokens;
        _validator = validator;
    }

    public Task<StaffSecuritySettings> SettingsAsync(CancellationToken cancellationToken) =>
        _db.StaffSecuritySettings.AsNoTracking().FirstAsync(cancellationToken);

    /// <summary>Opens a session and saves it. The refresh token exists only in the returned value.</summary>
    public async Task<StudioSessionTokens> OpenAsync(
        ApplicationUser user,
        StaffProfile profile,
        bool twoStepVerified,
        StudioClientInfo client,
        CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var refreshToken = StaffSecrets.NewToken();
        var stampHash = StaffSecrets.StampHash(user.SecurityStamp);

        var session = new StaffSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RefreshTokenHash = StaffSecrets.Hash(refreshToken),
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
            ExpiresAtUtc = now.AddHours(settings.SessionLifetimeHours),
            IpAddress = Clip(client.IpAddress, 45),
            UserAgent = Clip(client.UserAgent, 256),
            TwoStepVerified = twoStepVerified,
            StampHash = stampHash
        };

        _db.StaffSessions.Add(session);
        profile.LastActiveAtUtc = now;

        await _db.SaveChangesAsync(cancellationToken);

        var (accessToken, accessExpires) = _tokens.AccessToken(user.Id, user.UserName!, session.Id, stampHash, now);
        return new StudioSessionTokens(session.Id, accessToken, accessExpires, refreshToken, session.ExpiresAtUtc);
    }

    /// <summary>
    /// Re-keys an existing session after its own holder changed the security stamp (password,
    /// 2-step), so this device carries on while every other one is over.
    /// </summary>
    public async Task<StudioSessionTokens?> CarryOverAsync(
        ApplicationUser user,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await _db.StaffSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.UserId == user.Id && s.RevokedAtUtc == null, cancellationToken);

        if (session is null)
            return null;

        var now = DateTime.UtcNow;
        var refreshToken = StaffSecrets.NewToken();

        session.PreviousRefreshTokenHash = null;
        session.RefreshTokenHash = StaffSecrets.Hash(refreshToken);
        session.RotatedAtUtc = now;
        session.LastSeenAtUtc = now;
        session.StampHash = StaffSecrets.StampHash(user.SecurityStamp);

        await _db.SaveChangesAsync(cancellationToken);
        _validator.Forget(user.Id);

        var (accessToken, accessExpires) = _tokens.AccessToken(user.Id, user.UserName!, session.Id, session.StampHash, now);
        return new StudioSessionTokens(session.Id, accessToken, accessExpires, refreshToken, session.ExpiresAtUtc);
    }

    /// <summary>
    /// Ends every live session of the member (but <paramref name="except"/>), and forgets the cached
    /// answers so this server refuses their tokens immediately. Runs inside the caller's transaction.
    /// </summary>
    public async Task<int> EndAllAsync(
        Guid userId,
        string reason,
        Guid? byUserId,
        CancellationToken cancellationToken,
        Guid? except = null)
    {
        var now = DateTime.UtcNow;

        var ended = await _db.StaffSessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && (except == null || s.Id != except))
            .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.RevokedAtUtc, now)
                    .SetProperty(s => s.RevokedReason, reason)
                    .SetProperty(s => s.RevokedByUserId, byUserId),
                cancellationToken);

        _validator.Forget(userId);
        return ended;
    }

    public async Task<bool> EndOneAsync(Guid userId, Guid sessionId, string reason, Guid? byUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var ended = await _db.StaffSessions
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAtUtc == null)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.RevokedAtUtc, now)
                    .SetProperty(s => s.RevokedReason, reason)
                    .SetProperty(s => s.RevokedByUserId, byUserId),
                cancellationToken);

        _validator.Forget(userId);
        return ended > 0;
    }

    /// <summary>
    /// Revokes the member's refresh tokens on the <i>old</i> sign-in (<c>/api/auth</c>).
    /// <para>
    /// Since cutover a content-team account cannot obtain one of those in the first place. This
    /// still runs, and still matters, for the tokens minted before that day: a member suspended
    /// now may be holding a refresh token issued while the old door was open, and revoking it is
    /// the only thing that ends it early. Their access token there runs out on its own schedule.
    /// </para>
    /// </summary>
    public Task<int> EndLegacySignInsAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        return _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.ReasonRevoked, reason),
                cancellationToken);
    }

    /// <summary>Drops this server's cached answers about the member, so a change applies on their next request.</summary>
    public void ForgetCached(Guid userId) => _validator.Forget(userId);

    /// <summary>Stages a sign-in history row; it commits with the caller's next save.</summary>
    public void Record(Guid userId, StaffSignInOutcome outcome, StudioClientInfo client) =>
        _db.StaffSignInEvents.Add(new StaffSignInEvent
        {
            UserId = userId,
            OccurredAtUtc = DateTime.UtcNow,
            Outcome = outcome,
            IpAddress = Clip(client.IpAddress, 45),
            UserAgent = Clip(client.UserAgent, 256)
        });

    public static string? Clip(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
