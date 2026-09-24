using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Staff;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// A per-user generation number in the shared memory cache. Every cached answer about a user is
/// keyed with the generation it was computed under, so bumping it drops them all at once without
/// having to know which keys exist.
/// </summary>
internal static class CacheGenerations
{
    public static long Current(IMemoryCache cache, string scope, Guid userId) =>
        cache.TryGetValue(Key(scope, userId), out long generation) ? generation : 0;

    public static void Bump(IMemoryCache cache, string scope, Guid userId)
    {
        var key = Key(scope, userId);
        var next = (cache.TryGetValue(key, out long generation) ? generation : 0) + 1;

        // Outlives every entry it guards, so a bump is never forgotten while a stale entry remains.
        cache.Set(key, next, TimeSpan.FromMinutes(10));
    }

    private static string Key(string scope, Guid userId) => $"{scope}:gen:{userId:N}";
}

/// <inheritdoc cref="IStudioSessionValidator"/>
public class StudioSessionValidator : IStudioSessionValidator
{
    private const string Scope = "studio-session";

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public StudioSessionValidator(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<StudioSessionState?> ValidateAsync(
        Guid userId,
        Guid sessionId,
        string stampHash,
        CancellationToken cancellationToken = default)
    {
        var key = $"{Scope}:{sessionId:N}:{CacheGenerations.Current(_cache, Scope, userId)}";

        if (!_cache.TryGetValue(key, out Snapshot? snapshot))
        {
            snapshot = await LoadAsync(sessionId, cancellationToken);
            _cache.Set(key, snapshot, StaffSecrets.ValidationCacheWindow);
        }

        if (snapshot is null || snapshot.UserId != userId || !StaffSecrets.SameHash(snapshot.StampHash, stampHash))
            return null;

        // Re-checked on every read, not just at load: a cached answer must not outlive the session.
        var now = DateTime.UtcNow;
        if (now >= snapshot.ExpiresAtUtc || now >= snapshot.IdleExpiresAtUtc)
            return null;

        return snapshot.State;
    }

    public void Forget(Guid userId) => CacheGenerations.Bump(_cache, Scope, userId);

    /// <summary>Null when the session or the account behind it no longer entitles anyone to anything.</summary>
    private async Task<Snapshot?> LoadAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var row = await (
                from session in _db.StaffSessions.AsNoTracking()
                join profile in _db.StaffProfiles.AsNoTracking() on session.UserId equals profile.UserId
                join user in _db.Users.AsNoTracking() on session.UserId equals user.Id
                where session.Id == sessionId && session.RevokedAtUtc == null
                select new
                {
                    session.UserId,
                    session.ExpiresAtUtc,
                    session.LastSeenAtUtc,
                    profile.Status,
                    profile.StudioRole,
                    user.SecurityStamp,
                    user.TwoFactorEnabled
                })
            .FirstOrDefaultAsync(cancellationToken);

        // Suspension and deactivation arrive through the status. A lockout from wrong passwords is
        // deliberately not a reason to refuse: it guards new sign-ins, and five bad guesses by
        // somebody else must not be able to throw the real member out of their work.
        if (row is null || row.Status != StaffStatus.Active)
            return null;

        var settings = await _db.StaffSecuritySettings.AsNoTracking()
            .Select(s => new { s.RequireTwoStep, s.IdleTimeoutHours })
            .FirstAsync(cancellationToken);

        var fullAccess = !settings.RequireTwoStep || row.TwoFactorEnabled;

        return new Snapshot(
            row.UserId,
            StaffSecrets.StampHash(row.SecurityStamp),
            row.ExpiresAtUtc,
            row.LastSeenAtUtc.AddHours(settings.IdleTimeoutHours),
            new StudioSessionState(row.UserId, sessionId, row.StudioRole, fullAccess));
    }

    private sealed record Snapshot(
        Guid UserId,
        string StampHash,
        DateTime ExpiresAtUtc,
        DateTime IdleExpiresAtUtc,
        StudioSessionState State);
}

/// <inheritdoc cref="IPrivilegedTokenValidator"/>
public class PrivilegedTokenValidator : IPrivilegedTokenValidator
{
    private const string Scope = "privileged-token";

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public PrivilegedTokenValidator(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<bool> IsStillValidAsync(
        Guid userId,
        string? stampHash,
        IReadOnlyCollection<string> privilegedRoles,
        CancellationToken cancellationToken = default)
    {
        // A privileged token minted before this check existed carries no stamp. It is refused, and
        // the console's refresh mints one that does — a one-off re-sign-in at most.
        if (string.IsNullOrEmpty(stampHash))
            return false;

        var key = $"{Scope}:{userId:N}:{CacheGenerations.Current(_cache, Scope, userId)}";

        if (!_cache.TryGetValue(key, out Account? account))
        {
            account = await LoadAsync(userId, cancellationToken);
            _cache.Set(key, account, StaffSecrets.ValidationCacheWindow);
        }

        // Lockout is deliberately not checked: it guards new sign-ins, and letting five wrong
        // guesses at an admin's password end the admin's own working session would hand anyone a
        // way to throw them out of the console.
        return account is not null
               && StaffSecrets.SameHash(account.StampHash, stampHash)
               && privilegedRoles.All(role => account.Roles.Contains(role));
    }

    public void Forget(Guid userId) => CacheGenerations.Bump(_cache, Scope, userId);

    private async Task<Account?> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var stamp = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SecurityStamp })
            .FirstOrDefaultAsync(cancellationToken);

        if (stamp is null)
            return null;

        var roles = await (
                from userRole in _db.UserRoles.AsNoTracking()
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where userRole.UserId == userId
                select role.Name!)
            .ToListAsync(cancellationToken);

        return new Account(StaffSecrets.StampHash(stamp.SecurityStamp), roles.ToHashSet(StringComparer.Ordinal));
    }

    private sealed record Account(string StampHash, HashSet<string> Roles);
}
