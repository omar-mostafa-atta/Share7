using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Common.Models;
using Share7.Application.Feed;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Social;
using Share7.Domain.Feed;
using Share7.Domain.Organizations;
using Share7.Domain.Social;
using Share7.Infrastructure.Assessment;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Social;

public class SocialConsent : ISocialConsent
{
    private readonly ApplicationDbContext _dbContext;

    public SocialConsent(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<bool> MayHaveFriendsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await WhoMayHaveFriendsAsync([userId], cancellationToken)).Contains(userId);

    public async Task<IReadOnlySet<Guid>> WhoMayHaveFriendsAsync(IReadOnlyCollection<Guid> users, CancellationToken cancellationToken = default)
    {
        if (users.Count == 0)
            return new HashSet<Guid>();

        var ids = users.Distinct().ToList();

        // The same age line, and the same "unknown is a minor" rule, as calibration consent.
        var adults = await _dbContext.StudentProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId) && p.Age >= ExamOutcomeService.SelfConsentAge)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

        // Any verified, unrevoked guardian link that carries the scope.
        var consented = await _dbContext.GuardianLinks.AsNoTracking()
            .Where(g => ids.Contains(g.LearnerUserId)
                        && g.VerifiedAtUtc != null
                        && g.RevokedAtUtc == null
                        && (g.ConsentScope & GuardianConsentScope.SocialPlay) == GuardianConsentScope.SocialPlay)
            .Select(g => g.LearnerUserId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var restricted = await _dbContext.SocialRestrictions.Where(r => ids.Contains(r.UserId)
            && r.RevokedAtUtc == null && r.StartsAtUtc <= now && (r.ExpiresAtUtc == null || r.ExpiresAtUtc > now))
            .Select(r => r.UserId).ToListAsync(cancellationToken);
        return adults.Concat(consented).Except(restricted).ToHashSet();
    }
}

/// <summary>
/// Friendships that still hold: both rows present, and both players still allowed friends. A guardian
/// withdrawing <c>SocialPlay</c> ends every friendship the child has at once, without deleting the
/// record of it — granted again, they come back.
/// </summary>
public class FriendGraph : IFriendGraph
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISocialConsent _consent;

    public FriendGraph(ApplicationDbContext dbContext, ISocialConsent consent)
    {
        _dbContext = dbContext;
        _consent = consent;
    }

    public async Task<bool> AreFriendsAsync(Guid a, Guid b, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Friendships.AsNoTracking().AnyAsync(f => f.UserId == a && f.FriendUserId == b, cancellationToken))
            return false;

        return (await _consent.WhoMayHaveFriendsAsync([a, b], cancellationToken)).Count == 2;
    }

    public async Task<IReadOnlyList<Guid>> FriendsOfAsync(Guid user, CancellationToken cancellationToken = default)
    {
        if (!await _consent.MayHaveFriendsAsync(user, cancellationToken))
            return [];

        var friends = await _dbContext.Friendships.AsNoTracking()
            .Where(f => f.UserId == user)
            .Select(f => f.FriendUserId)
            .Take(SocialPolicy.MaxConnections)
            .ToListAsync(cancellationToken);

        var allowed = await _consent.WhoMayHaveFriendsAsync(friends, cancellationToken);
        return friends.Where(allowed.Contains).ToList();
    }
}

public class FriendService : IFriendService
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 8;

    private readonly ApplicationDbContext _dbContext;
    private readonly ISocialConsent _consent;
    private readonly IBlockList _blocks;
    private readonly IRosterNameResolver _names;
    private readonly IPlayerEventPublisher _events;
    private readonly ILogger<FriendService> _logger;

    public FriendService(
        ApplicationDbContext dbContext,
        ISocialConsent consent,
        IBlockList blocks,
        IRosterNameResolver names,
        IPlayerEventPublisher events,
        ILogger<FriendService> logger)
    {
        _dbContext = dbContext;
        _consent = consent;
        _blocks = blocks;
        _names = names;
        _events = events;
        _logger = logger;
    }

    // ---- codes ---------------------------------------------------------------------------------

    public async Task<ServiceResult<FriendCodeDto>> GetCodeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _consent.MayHaveFriendsAsync(userId, cancellationToken))
            return ConsentRequired<FriendCodeDto>();

        var existing = await _dbContext.PlayerFriendCodes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        return existing is not null
            ? ServiceResult<FriendCodeDto>.Success(ToDto(existing))
            : await MintAsync(userId, replace: false, cancellationToken);
    }

    public async Task<ServiceResult<FriendCodeDto>> RotateCodeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _consent.MayHaveFriendsAsync(userId, cancellationToken))
            return ConsentRequired<FriendCodeDto>();

        return await MintAsync(userId, replace: true, cancellationToken);
    }

    private async Task<ServiceResult<FriendCodeDto>> MintAsync(Guid userId, bool replace, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var code = NewCode();
            var now = DateTime.UtcNow;

            try
            {
                // Replace-or-insert in one statement, so a double tap cannot leave two rows or none.
                await _dbContext.Database.ExecuteSqlRawAsync(
                    replace
                        ? """
                          UPDATE [PlayerFriendCodes] SET [Code] = {1}, [CreatedAtUtc] = {2} WHERE [UserId] = {0};
                          IF @@ROWCOUNT = 0 INSERT INTO [PlayerFriendCodes] ([UserId], [Code], [CreatedAtUtc]) VALUES ({0}, {1}, {2});
                          """
                        : """
                          IF NOT EXISTS (SELECT 1 FROM [PlayerFriendCodes] WHERE [UserId] = {0})
                              INSERT INTO [PlayerFriendCodes] ([UserId], [Code], [CreatedAtUtc]) VALUES ({0}, {1}, {2});
                          """,
                    [userId, code, now],
                    cancellationToken);
            }
            catch (SqlException exception) when (exception.Number is 2601 or 2627 && attempt < 2)
            {
                // Either another player's code (at 32^8, astonishing) or this player's first mint
                // racing itself; both are settled by trying again.
                continue;
            }

            var stored = await _dbContext.PlayerFriendCodes.AsNoTracking().FirstAsync(c => c.UserId == userId, cancellationToken);
            return ServiceResult<FriendCodeDto>.Success(ToDto(stored));
        }
    }

    private static string NewCode() =>
        string.Create(CodeLength, Alphabet, (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = source[RandomNumberGenerator.GetInt32(source.Length)];
        });

    // ---- requests ------------------------------------------------------------------------------

    public async Task<ServiceResult<FriendRequestDto>> AddByCodeAsync(
        Guid userId, AddFriendRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _consent.MayHaveFriendsAsync(userId, cancellationToken))
            return ConsentRequired<FriendRequestDto>();

        var code = new string((request.Code ?? string.Empty)
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .Select(char.ToUpperInvariant)
            .ToArray());

        var owner = code.Length is > 0 and <= 12
            ? await _dbContext.PlayerFriendCodes.AsNoTracking()
                .Where(c => c.Code == code)
                .Select(c => (Guid?)c.UserId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (owner == userId)
            return ServiceResult<FriendRequestDto>.Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "That is your own code.");

        // Unknown, rotated away, an owner who may no longer have friends, or a block either way: one
        // answer for all of them.
        if (owner is not { } target
            || !await _consent.MayHaveFriendsAsync(target, cancellationToken)
            || await _dbContext.SocialPrivacy.AnyAsync(p => p.UserId == target && !p.FriendRequests, cancellationToken)
            || await _blocks.IsBlockedEitherWayAsync(userId, target, cancellationToken))
            return ServiceResult<FriendRequestDto>.Failure(
                ApiErrors.FriendCodeNotFound, ServiceErrorKind.NotFound, "No player can be added with that code.");

        var now = DateTime.UtcNow;

        if (await _dbContext.Friendships.AnyAsync(f => f.UserId == userId && f.FriendUserId == target, cancellationToken))
            return ServiceResult<FriendRequestDto>.Success(await AcceptedViewAsync(userId, target, now, cancellationToken));

        // They had already asked this player: entering their code is the answer.
        var theirs = await _dbContext.FriendRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.SenderUserId == target && r.RecipientUserId == userId
                                                          && r.State == FriendRequestState.Pending, cancellationToken);

        if (theirs is not null)
            return await AcceptAsync(userId, theirs.Id, cancellationToken);

        if (await PendingAsync(userId, target, cancellationToken) is { } mine)
            return ServiceResult<FriendRequestDto>.Success(await MapAsync(mine, cancellationToken));

        var friendRequest = new FriendRequest
        {
            Id = Guid.NewGuid(),
            SenderUserId = userId,
            RecipientUserId = target,
            State = FriendRequestState.Pending,
            CreatedAtUtc = now
        };

        var fromName = (await _names.ResolveAsync([userId], cancellationToken)).GetValueOrDefault(userId);

        _dbContext.FriendRequests.Add(friendRequest);
        _events.Stage(target, PlayerEventTypes.FriendRequestReceived,
            new { requestId = friendRequest.Id, fromUserId = userId, fromDisplayName = fromName });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            Detach();

            if (await PendingAsync(userId, target, cancellationToken) is { } raced)
                return ServiceResult<FriendRequestDto>.Success(await MapAsync(raced, cancellationToken));

            throw;
        }

        return ServiceResult<FriendRequestDto>.Success(await MapAsync(friendRequest, cancellationToken));
    }

    public async Task<ServiceResult<IReadOnlyList<FriendRequestDto>>> RequestsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.FriendRequests.AsNoTracking()
            .Where(r => r.RecipientUserId == userId && r.State == FriendRequestState.Pending)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var names = await _names.ResolveAsync(
            rows.SelectMany(r => new[] { r.SenderUserId, r.RecipientUserId }).Distinct().ToList(), cancellationToken);

        IReadOnlyList<FriendRequestDto> list = rows.Select(r => ToDto(r, names)).ToList();
        return ServiceResult<IReadOnlyList<FriendRequestDto>>.Success(list);
    }

    public async Task<ServiceResult<FriendRequestDto>> AcceptAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _dbContext.FriendRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId && r.RecipientUserId == userId, cancellationToken);

        if (request is null)
            return RequestNotFound(requestId);

        if (request.State == FriendRequestState.Accepted)
            return ServiceResult<FriendRequestDto>.Success(await MapAsync(request, cancellationToken));

        if (request.State != FriendRequestState.Pending)
            return NotPending(request.State);

        if (!await _consent.MayHaveFriendsAsync(userId, cancellationToken))
            return ConsentRequired<FriendRequestDto>();

        // The sender may have lost consent, or one may have blocked the other, since they asked.
        if (!await _consent.MayHaveFriendsAsync(request.SenderUserId, cancellationToken)
            || await _blocks.IsBlockedEitherWayAsync(userId, request.SenderUserId, cancellationToken))
        {
            await RetireAsync(requestId, FriendRequestState.Cancelled, cancellationToken);
            return NotPending(FriendRequestState.Cancelled);
        }

        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (!await RetireAsync(requestId, FriendRequestState.Accepted, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            var current = await _dbContext.FriendRequests.AsNoTracking().FirstAsync(r => r.Id == requestId, cancellationToken);

            return current.State == FriendRequestState.Accepted
                ? ServiceResult<FriendRequestDto>.Success(await MapAsync(current, cancellationToken))
                : NotPending(current.State);
        }

        // Both directions, each only if absent — two accepts racing (each side entering the other's
        // code) make one friendship, not a key violation.
        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM [Friendships] WHERE [UserId] = {0} AND [FriendUserId] = {1})
                INSERT INTO [Friendships] ([UserId], [FriendUserId], [CreatedAtUtc]) VALUES ({0}, {1}, {2});
            IF NOT EXISTS (SELECT 1 FROM [Friendships] WHERE [UserId] = {1} AND [FriendUserId] = {0})
                INSERT INTO [Friendships] ([UserId], [FriendUserId], [CreatedAtUtc]) VALUES ({1}, {0}, {2});
            """,
            [userId, request.SenderUserId, now],
            cancellationToken);

        var byName = (await _names.ResolveAsync([userId], cancellationToken)).GetValueOrDefault(userId);
        _events.Stage(request.SenderUserId, PlayerEventTypes.FriendRequestAccepted,
            new { requestId, byUserId = userId, byDisplayName = byName });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Friend request {RequestId} accepted.", requestId);

        var accepted = await _dbContext.FriendRequests.AsNoTracking().FirstAsync(r => r.Id == requestId, cancellationToken);
        return ServiceResult<FriendRequestDto>.Success(await MapAsync(accepted, cancellationToken));
    }

    public async Task<ServiceResult<FriendRequestDto>> DeclineAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _dbContext.FriendRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId && r.RecipientUserId == userId, cancellationToken);

        if (request is null)
            return RequestNotFound(requestId);

        if (!await RetireAsync(requestId, FriendRequestState.Declined, cancellationToken) && request.State != FriendRequestState.Declined)
            return NotPending(request.State);

        var current = await _dbContext.FriendRequests.AsNoTracking().FirstAsync(r => r.Id == requestId, cancellationToken);
        return ServiceResult<FriendRequestDto>.Success(await MapAsync(current, cancellationToken));
    }

    public async Task<ServiceResult> RemoveAsync(Guid userId, Guid friendUserId, CancellationToken cancellationToken = default)
    {
        await _dbContext.Friendships
            .Where(f => (f.UserId == userId && f.FriendUserId == friendUserId)
                        || (f.UserId == friendUserId && f.FriendUserId == userId))
            .ExecuteDeleteAsync(cancellationToken);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<FriendRequestDto>> CancelAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _dbContext.FriendRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId && r.SenderUserId == userId, cancellationToken);
        if (request is null) return RequestNotFound(requestId);
        if (!await RetireAsync(requestId, FriendRequestState.Cancelled, cancellationToken) && request.State != FriendRequestState.Cancelled)
            return NotPending(request.State);
        var current = await _dbContext.FriendRequests.AsNoTracking().FirstAsync(r => r.Id == requestId, cancellationToken);
        return ServiceResult<FriendRequestDto>.Success(await MapAsync(current, cancellationToken));
    }

    // ---- helpers -------------------------------------------------------------------------------

    private Task<FriendRequest?> PendingAsync(Guid sender, Guid recipient, CancellationToken cancellationToken) =>
        _dbContext.FriendRequests.AsNoTracking().FirstOrDefaultAsync(
            r => r.SenderUserId == sender && r.RecipientUserId == recipient && r.State == FriendRequestState.Pending,
            cancellationToken);

    private async Task<bool> RetireAsync(Guid requestId, FriendRequestState to, CancellationToken cancellationToken) =>
        await _dbContext.FriendRequests
            .Where(r => r.Id == requestId && r.State == FriendRequestState.Pending)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.State, to)
                .SetProperty(r => r.AnsweredAtUtc, DateTime.UtcNow), cancellationToken) > 0;

    /// <summary>What "add" answers when the two are already friends: the friendship, shaped as an accepted request.</summary>
    private async Task<FriendRequestDto> AcceptedViewAsync(Guid userId, Guid friend, DateTime now, CancellationToken cancellationToken)
    {
        var names = await _names.ResolveAsync([userId, friend], cancellationToken);

        return new FriendRequestDto
        {
            Id = Guid.Empty,
            FromUserId = userId,
            FromDisplayName = names.GetValueOrDefault(userId),
            ToUserId = friend,
            ToDisplayName = names.GetValueOrDefault(friend),
            State = FriendRequestState.Accepted,
            CreatedAtUtc = now
        };
    }

    private async Task<FriendRequestDto> MapAsync(FriendRequest request, CancellationToken cancellationToken) =>
        ToDto(request, await _names.ResolveAsync([request.SenderUserId, request.RecipientUserId], cancellationToken));

    private static FriendRequestDto ToDto(FriendRequest r, IReadOnlyDictionary<Guid, string> names) => new()
    {
        Id = r.Id,
        FromUserId = r.SenderUserId,
        FromDisplayName = names.GetValueOrDefault(r.SenderUserId),
        ToUserId = r.RecipientUserId,
        ToDisplayName = names.GetValueOrDefault(r.RecipientUserId),
        State = r.State,
        CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc)
    };

    private static FriendCodeDto ToDto(PlayerFriendCode code) => new()
    {
        Code = code.Code,
        CreatedAtUtc = DateTime.SpecifyKind(code.CreatedAtUtc, DateTimeKind.Utc)
    };

    private void Detach()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    private static ServiceResult<T> ConsentRequired<T>() =>
        ServiceResult<T>.Failure(
            ApiErrors.SocialConsentRequired,
            ServiceErrorKind.Forbidden,
            "Playing with friends needs a parent or guardian to turn it on. Classmates work without it.");

    private static ServiceResult<FriendRequestDto> RequestNotFound(Guid requestId) =>
        ServiceResult<FriendRequestDto>.Failure(ApiErrors.FriendRequestNotFound, ServiceErrorKind.NotFound, $"Friend request {requestId} was not found.");

    private static ServiceResult<FriendRequestDto> NotPending(FriendRequestState state) =>
        ServiceResult<FriendRequestDto>.Failure(
            ApiErrors.FriendRequestNotPending,
            ServiceErrorKind.Conflict,
            $"The request is {state}.",
            new Dictionary<string, object?> { ["state"] = state.ToString() });
}
