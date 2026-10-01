using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Common.Models;
using Share7.Application.Feed;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Social;
using Share7.Domain.Feed;
using Share7.Domain.Multiplayer;
using Share7.Domain.Social;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Social;

public class SocialService : ISocialService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISocialPolicy _policy;
    private readonly IPresenceReader _presence;
    private readonly IRosterNameResolver _names;
    private readonly IPlayerEventPublisher _events;
    private readonly ILogger<SocialService> _logger;

    public SocialService(
        ApplicationDbContext dbContext,
        ISocialPolicy policy,
        IPresenceReader presence,
        IRosterNameResolver names,
        IPlayerEventPublisher events,
        ILogger<SocialService> logger)
    {
        _dbContext = dbContext;
        _policy = policy;
        _presence = presence;
        _names = names;
        _events = events;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<ConnectionDto>>> ConnectionsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        // Presence is the most sensitive thing on this list, so the list is exactly who may see it.
        var connections = await _policy.ConnectionsAsync(userId, SocialAction.SeePresence, cancellationToken);
        var ids = connections.Select(c => c.UserId).ToList();

        var presence = await _presence.GetAsync(ids, cancellationToken);
        var names = await _names.ResolveAsync(ids, cancellationToken);

        IReadOnlyList<ConnectionDto> list = connections
            .Select(c => new ConnectionDto
            {
                UserId = c.UserId,
                DisplayName = names.GetValueOrDefault(c.UserId),
                Relation = c.Relation,
                Presence = presence.GetValueOrDefault(c.UserId)
            })
            // Who can play now first, then by name — the order a child scans for someone to invite.
            .OrderByDescending(c => c.Presence is PlayerPresenceState.Online or PlayerPresenceState.InLobby)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ServiceResult<IReadOnlyList<ConnectionDto>>.Success(list);
    }

    public async Task<ServiceResult<IReadOnlyList<BlockedPlayerDto>>> BlocksAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.PlayerBlocks
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var names = await _names.ResolveAsync(rows.Select(r => r.BlockedUserId).ToList(), cancellationToken);

        IReadOnlyList<BlockedPlayerDto> list = rows
            .Select(r => new BlockedPlayerDto
            {
                UserId = r.BlockedUserId,
                DisplayName = names.GetValueOrDefault(r.BlockedUserId),
                BlockedAtUtc = DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc)
            })
            .ToList();

        return ServiceResult<IReadOnlyList<BlockedPlayerDto>>.Success(list);
    }

    public async Task<ServiceResult> BlockAsync(Guid userId, Guid target, CancellationToken cancellationToken = default)
    {
        if (target == userId || target == Guid.Empty)
            return ServiceResult.Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "You cannot block yourself.");

        // Any id succeeds, and an id with no account behind it writes nothing: blocking must not be
        // a way to learn which ids are real.
        if (!await _dbContext.Users.AnyAsync(u => u.Id == target, cancellationToken))
            return ServiceResult.Success();

        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM [PlayerBlocks] WHERE [UserId] = {0} AND [BlockedUserId] = {1})
                    INSERT INTO [PlayerBlocks] ([UserId], [BlockedUserId], [CreatedAtUtc]) VALUES ({0}, {1}, {2});
                """,
                [userId, target, now],
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            // A second block racing the first — same block.
        }

        // Every invite between the two, either way, is withdrawn: an invite from someone you just
        // blocked must not still be sitting in your list, and yours must not still be in theirs.
        var pending = await _dbContext.SessionInvitations
            .Where(i => i.State == InvitationState.Pending
                        && ((i.SenderUserId == userId && i.RecipientUserId == target)
                            || (i.SenderUserId == target && i.RecipientUserId == userId)))
            .ToListAsync(cancellationToken);

        foreach (var invitation in pending)
        {
            invitation.State = InvitationState.Cancelled;
            invitation.AnsweredAtUtc = now;

            _events.Stage(invitation.RecipientUserId, PlayerEventTypes.InviteCancelled,
                new { invitationId = invitation.Id, sessionId = invitation.SessionId }, invitation.ExpiresAtUtc);
        }

        // A block ends a friendship, both sides, and any friend request still waiting between them.
        await _dbContext.Friendships
            .Where(f => (f.UserId == userId && f.FriendUserId == target) || (f.UserId == target && f.FriendUserId == userId))
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.FriendRequests
            .Where(r => r.State == FriendRequestState.Pending
                        && ((r.SenderUserId == userId && r.RecipientUserId == target)
                            || (r.SenderUserId == target && r.RecipientUserId == userId)))
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.State, FriendRequestState.Cancelled)
                .SetProperty(r => r.AnsweredAtUtc, now), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Player {UserId} blocked a player; {Invites} pending invites withdrawn.", userId, pending.Count);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UnblockAsync(Guid userId, Guid target, CancellationToken cancellationToken = default)
    {
        await _dbContext.PlayerBlocks
            .Where(b => b.UserId == userId && b.BlockedUserId == target)
            .ExecuteDeleteAsync(cancellationToken);

        return ServiceResult.Success();
    }
}
