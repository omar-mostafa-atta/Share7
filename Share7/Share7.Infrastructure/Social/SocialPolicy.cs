using Microsoft.EntityFrameworkCore;
using Share7.Application.Social;
using Share7.Domain.Organizations;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Social;

/// <summary>
/// Today's rule, in one place — see <see cref="ISocialPolicy"/>. Classmates or friends, never a
/// stranger, a block in either direction overriding both, and real names to nobody.
/// </summary>
public class SocialPolicy : ISocialPolicy
{
    /// <summary>The most connections a "play with…" list carries. A class is thirty; this is headroom.</summary>
    public const int MaxConnections = 200;

    private readonly ApplicationDbContext _dbContext;
    private readonly IBlockList _blocks;
    private readonly IFriendGraph _friends;

    public SocialPolicy(ApplicationDbContext dbContext, IBlockList blocks, IFriendGraph friends)
    {
        _dbContext = dbContext;
        _blocks = blocks;
        _friends = friends;
    }

    public async Task<SocialPermission> CanInteractAsync(
        Guid actor, Guid target, SocialAction action, CancellationToken cancellationToken = default)
    {
        if (actor == target)
            return SocialPermission.Deny("self");

        // Not until friendships carry guardian consent to it. The handle is the only name anyone sees.
        if (action == SocialAction.SeeRealName)
            return SocialPermission.Deny("real_names_off");

        // Checked first, and reported as "not connected" — never as "blocked" — so a block cannot be
        // detected by probing.
        if (await _blocks.IsBlockedEitherWayAsync(actor, target, cancellationToken))
            return SocialPermission.Deny("not_connected");

        if (await ClassmatesQuery(actor).AnyAsync(id => id == target, cancellationToken))
            return SocialPermission.Allow("classmate");

        if (await _friends.AreFriendsAsync(actor, target, cancellationToken))
            return SocialPermission.Allow("friend");

        return SocialPermission.Deny("not_connected");
    }

    public async Task<IReadOnlyList<SocialConnection>> ConnectionsAsync(
        Guid actor, SocialAction action, CancellationToken cancellationToken = default)
    {
        if (action == SocialAction.SeeRealName)
            return [];

        var blocked = await _blocks.BlockedEitherWayAsync(actor, cancellationToken);

        var classmates = await ClassmatesQuery(actor)
            .Distinct()
            .Take(MaxConnections)
            .ToListAsync(cancellationToken);

        var friends = await _friends.FriendsOfAsync(actor, cancellationToken);

        // A friend who is also a classmate is listed once, as a friend — the closer relation.
        var connections = new Dictionary<Guid, SocialRelation>();

        foreach (var id in classmates.Where(id => id != actor && !blocked.Contains(id)))
            connections[id] = SocialRelation.Classmate;

        foreach (var id in friends.Where(id => id != actor && !blocked.Contains(id)))
            connections[id] = SocialRelation.Friend;

        return connections
            .Take(MaxConnections)
            .Select(c => new SocialConnection(c.Key, c.Value))
            .ToList();
    }

    /// <summary>
    /// Learners sharing an active class of an active organization with <paramref name="actor"/>, who
    /// is a learner in it too. A teacher's classes are not the teacher's playmates.
    /// </summary>
    private IQueryable<Guid> ClassmatesQuery(Guid actor) =>
        from mine in _dbContext.CohortMemberships
        where mine.UserId == actor && mine.LeftAtUtc == null && mine.Role == CohortRole.Learner
        join cohort in _dbContext.Cohorts on mine.CohortId equals cohort.Id
        where cohort.Status == CohortStatus.Active
        join org in _dbContext.Organizations on cohort.OrgId equals org.Id
        where org.Status == OrganizationStatus.Active
        join theirs in _dbContext.CohortMemberships on cohort.Id equals theirs.CohortId
        where theirs.LeftAtUtc == null && theirs.Role == CohortRole.Learner && theirs.UserId != actor
        select theirs.UserId;
}

public class BlockList : IBlockList
{
    private readonly ApplicationDbContext _dbContext;

    public BlockList(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken cancellationToken = default) =>
        _dbContext.PlayerBlocks.AsNoTracking().AnyAsync(
            x => (x.UserId == a && x.BlockedUserId == b) || (x.UserId == b && x.BlockedUserId == a),
            cancellationToken);

    public async Task<IReadOnlySet<Guid>> BlockedEitherWayAsync(Guid user, CancellationToken cancellationToken = default)
    {
        var ids = await _dbContext.PlayerBlocks
            .AsNoTracking()
            .Where(x => x.UserId == user || x.BlockedUserId == user)
            .Select(x => x.UserId == user ? x.BlockedUserId : x.UserId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }
}
