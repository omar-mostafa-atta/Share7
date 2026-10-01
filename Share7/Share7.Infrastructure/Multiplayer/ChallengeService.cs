using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Share7.Application.Common.Models;
using Share7.Application.Feed;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Progress.Interfaces;
using Share7.Application.Social;
using Share7.Domain.Feed;
using Share7.Domain.Leaderboards;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Challenges. See <see cref="IChallengeService"/>.
/// <para>
/// <b>Decided from the results stream.</b> Every graded attempt with a score already writes a
/// <c>LESSON_BEST_PERCENT</c> result carrying that attempt's own percent, lesson, game and time —
/// flagged or unranked play excepted, by the rules that stream already applies. A challenge reads
/// the recipient's best of those inside its window. Nothing in the attempt path knows challenges
/// exist, which is what keeps this a consumer rather than one more thing grading has to do.
/// </para>
/// </summary>
public class ChallengeService : IChallengeService
{
    public const int DefaultDays = 3;
    public const int MaxDays = 7;

    /// <summary>How long an ended challenge stays on the list.</summary>
    private static readonly TimeSpan ShownAfterEnding = TimeSpan.FromDays(7);

    private const int SettleBatch = 100;

    private readonly ApplicationDbContext _dbContext;
    private readonly ISocialPolicy _policy;
    private readonly IUnlockService _unlocks;
    private readonly IRosterNameResolver _names;
    private readonly IPlayerEventPublisher _events;
    private readonly MultiplayerSessionService _sessions;
    private readonly SessionInvitationService _invitations;
    private readonly ILogger<ChallengeService> _logger;

    public ChallengeService(
        ApplicationDbContext dbContext,
        ISocialPolicy policy,
        IUnlockService unlocks,
        IRosterNameResolver names,
        IPlayerEventPublisher events,
        MultiplayerSessionService sessions,
        SessionInvitationService invitations,
        ILogger<ChallengeService> logger)
    {
        _dbContext = dbContext;
        _policy = policy;
        _unlocks = unlocks;
        _names = names;
        _events = events;
        _sessions = sessions;
        _invitations = invitations;
        _logger = logger;
    }

    // ---- send ----------------------------------------------------------------------------------

    public async Task<ServiceResult<ChallengeDto>> SendAsync(
        Guid userId, SendChallengeRequest request, CancellationToken cancellationToken = default)
    {
        var target = request.UserId;

        if (target == userId || target == Guid.Empty)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Choose someone else to challenge.");

        if (request.Days is < 1 or > MaxDays)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, $"A challenge lasts 1 to {MaxDays} days.");

        var permission = await _policy.CanInteractAsync(userId, target, SocialAction.Challenge, cancellationToken);

        if (!permission.Allowed)
            return Failure(ApiErrors.SocialNotAllowed, ServiceErrorKind.Forbidden, "You can only challenge classmates and friends.");

        // The bar: the challenger's own best, from their graded attempts. A challenge with no score
        // behind it would be "beat nothing".
        var bar = await _dbContext.UserLessonProgress
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.GameId == request.GameId && p.LessonId == request.LessonId)
            .Select(p => (int?)p.BestPercent)
            .FirstOrDefaultAsync(cancellationToken);

        if (bar is not > 0)
            return Failure(ApiErrors.ChallengeNoScore, ServiceErrorKind.Conflict, "Play the lesson first, then challenge someone to beat your score.");

        // A challenge on a lesson they cannot open could only ever be lost.
        var unlocked = await _unlocks.GetUnlockedNodeIdsAsync(target, request.GameId, cancellationToken);

        if (!unlocked.Contains(request.LessonId))
            return Failure(ApiErrors.ChallengeLessonLocked, ServiceErrorKind.Conflict, "They can't play that lesson yet.");

        var now = DateTime.UtcNow;

        // An open challenge past its deadline still holds the one-open slot until it is settled.
        await SettleForUserAsync(userId, now, cancellationToken);

        if (await OpenBetweenAsync(userId, target, request.LessonId, cancellationToken) is { } existing)
            return ServiceResult<ChallengeDto>.Success(await MapAsync(existing, userId, cancellationToken));

        var challenge = new Challenge
        {
            Id = Guid.NewGuid(),
            ChallengerUserId = userId,
            RecipientUserId = target,
            GameId = request.GameId,
            LessonId = request.LessonId,
            BarPercent = bar.Value,
            State = ChallengeState.Pending,
            Outcome = ChallengeOutcome.None,
            CreatedAtUtc = now,
            DeadlineUtc = now.AddDays(request.Days ?? DefaultDays)
        };

        var fromName = (await _names.ResolveAsync([userId], cancellationToken)).GetValueOrDefault(userId);

        _dbContext.Challenges.Add(challenge);
        _events.Stage(target, PlayerEventTypes.ChallengeReceived, new
        {
            challengeId = challenge.Id,
            fromUserId = userId,
            fromDisplayName = fromName,
            gameId = challenge.GameId,
            lessonId = challenge.LessonId,
            barPercent = challenge.BarPercent,
            deadlineUtc = challenge.DeadlineUtc
        }, challenge.DeadlineUtc);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            Detach();

            if (await OpenBetweenAsync(userId, target, request.LessonId, cancellationToken) is { } raced)
                return ServiceResult<ChallengeDto>.Success(await MapAsync(raced, userId, cancellationToken));

            throw;
        }

        return ServiceResult<ChallengeDto>.Success(await MapAsync(challenge, userId, cancellationToken));
    }

    // ---- read ----------------------------------------------------------------------------------

    public async Task<ServiceResult<IReadOnlyList<ChallengeDto>>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        await SettleForUserAsync(userId, now, cancellationToken);

        var since = now - ShownAfterEnding;

        var rows = await _dbContext.Challenges
            .AsNoTracking()
            .Where(c => (c.ChallengerUserId == userId || c.RecipientUserId == userId)
                        && (c.State == ChallengeState.Pending || c.State == ChallengeState.Accepted
                            || c.EndedAtUtc >= since))
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var names = await _names.ResolveAsync(
            rows.SelectMany(c => new[] { c.ChallengerUserId, c.RecipientUserId }).Distinct().ToList(), cancellationToken);

        IReadOnlyList<ChallengeDto> list = rows.Select(c => ToDto(c, userId, names, now)).ToList();
        return ServiceResult<IReadOnlyList<ChallengeDto>>.Success(list);
    }

    // ---- answer --------------------------------------------------------------------------------

    public async Task<ServiceResult<ChallengeDto>> AcceptAsync(Guid userId, Guid challengeId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        if (await FindAsync(challengeId, c => c.RecipientUserId == userId, cancellationToken) is not { } challenge)
            return NotFound(challengeId);

        await SettleAsync(challenge, now, cancellationToken);

        var moved = await _dbContext.Challenges
            .Where(c => c.Id == challengeId && c.State == ChallengeState.Pending && c.DeadlineUtc > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.State, ChallengeState.Accepted)
                .SetProperty(c => c.AcceptedAtUtc, now), cancellationToken) > 0;

        if (!moved)
            return await AnsweredAsync(challengeId, userId, ChallengeState.Accepted, cancellationToken);

        var byName = (await _names.ResolveAsync([userId], cancellationToken)).GetValueOrDefault(userId);

        _events.Stage(challenge.ChallengerUserId, PlayerEventTypes.ChallengeAccepted,
            new { challengeId, byUserId = userId, byDisplayName = byName }, challenge.DeadlineUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ReloadAsync(challengeId, userId, cancellationToken);
    }

    public async Task<ServiceResult<ChallengeDto>> DeclineAsync(Guid userId, Guid challengeId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(challengeId, c => c.RecipientUserId == userId, cancellationToken) is null)
            return NotFound(challengeId);

        // Like a declined invite, the challenger is not told: it simply lapses on their side.
        var moved = await EndAsync(challengeId, ChallengeState.Pending, ChallengeState.Declined, DateTime.UtcNow, cancellationToken);

        return moved
            ? await ReloadAsync(challengeId, userId, cancellationToken)
            : await AnsweredAsync(challengeId, userId, ChallengeState.Declined, cancellationToken);
    }

    public async Task<ServiceResult<ChallengeDto>> CancelAsync(Guid userId, Guid challengeId, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(challengeId, c => c.ChallengerUserId == userId, cancellationToken) is not { } challenge)
            return NotFound(challengeId);

        // Pending only. Withdrawing a challenge the other player has taken on — and may be about to
        // win — is exactly the move a sore loser would reach for.
        if (!await EndAsync(challengeId, ChallengeState.Pending, ChallengeState.Cancelled, DateTime.UtcNow, cancellationToken))
            return await AnsweredAsync(challengeId, userId, ChallengeState.Cancelled, cancellationToken);

        _events.Stage(challenge.RecipientUserId, PlayerEventTypes.ChallengeCancelled, new { challengeId }, challenge.DeadlineUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ReloadAsync(challengeId, userId, cancellationToken);
    }

    // ---- live ----------------------------------------------------------------------------------

    public async Task<ServiceResult<LiveChallengeDto>> SendLiveAsync(
        Guid userId, SendLiveChallengeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.UserId == userId || request.UserId == Guid.Empty)
            return ServiceResult<LiveChallengeDto>.Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Choose someone else to challenge.");

        // Asked before the room exists, so a refused challenge never leaves a reserved room behind.
        var permission = await _policy.CanInteractAsync(userId, request.UserId, SocialAction.Challenge, cancellationToken);

        if (!permission.Allowed)
            return ServiceResult<LiveChallengeDto>.Failure(ApiErrors.SocialNotAllowed, ServiceErrorKind.Forbidden, "You can only challenge classmates and friends.");

        var created = await _sessions.CreateReservedAsync(userId, new CreateMultiplayerSessionRequest
        {
            GameId = request.GameId,
            ModeKey = request.ModeKey,
            CurriculumPath = request.CurriculumPath,
            TransportSessionName = request.TransportSessionName,
            TransportRegion = request.TransportRegion,
            ProtocolVersion = request.ProtocolVersion,
            Visibility = SessionVisibility.Private,
            MaxPlayers = 2,

            // A duel between two classmates is play, not ranked: ranked must not be farmable by pairs.
            IsRanked = false,
            RequestId = request.RequestId
        }, [userId, request.UserId], MultiplayerOperations.LiveChallenge, cancellationToken);

        if (!created.Succeeded)
            return Relay<LiveChallengeDto>(created);

        var invited = await _invitations.InviteCoreAsync(
            userId, created.Value!.Id, request.UserId, InviteKinds.LiveChallenge, cancellationToken);

        if (!invited.Succeeded)
            return Relay<LiveChallengeDto>(invited);

        return ServiceResult<LiveChallengeDto>.Success(new LiveChallengeDto { Session = created.Value, Invitation = invited.Value! });
    }

    // ---- settlement ----------------------------------------------------------------------------

    public async Task<int> SettleDueAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Due = past its deadline, or accepted and already beaten. One query finds both, so a pass
        // over a quiet day costs one statement.
        var due = await (
                from c in Open()
                let best = _dbContext.GameResults
                    .Where(r => r.UserId == c.RecipientUserId
                                && r.Metric == LeaderboardMetrics.LessonBestPercent
                                && r.SourceType == GameResultSource.Attempt
                                && r.SourceId == c.LessonId
                                && r.GameId == c.GameId
                                && !r.IsFlagged
                                && r.OccurredAtUtc >= c.AcceptedAtUtc
                                && r.OccurredAtUtc <= c.DeadlineUtc)
                    .Max(r => (long?)r.Value)
                where c.DeadlineUtc <= now || (c.State == ChallengeState.Accepted && best > c.BarPercent)
                orderby c.DeadlineUtc
                select c)
            .Take(SettleBatch)
            .ToListAsync(cancellationToken);

        var settled = 0;

        foreach (var challenge in due)
        {
            try
            {
                if (await SettleAsync(challenge, now, cancellationToken))
                    settled++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One bad row must not stop the rest; it is retried next pass.
                Detach();
                _logger.LogError(exception, "Could not settle challenge {ChallengeId}.", challenge.Id);
            }
        }

        return settled;
    }

    private async Task SettleForUserAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var mine = await Open()
            .Where(c => c.ChallengerUserId == userId || c.RecipientUserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var challenge in mine)
            await SettleAsync(challenge, now, cancellationToken);
    }

    /// <summary>
    /// Decides one challenge if it is due, and records the recipient's best so far if not. True when
    /// this call ended it.
    /// </summary>
    private async Task<bool> SettleAsync(Challenge challenge, DateTime now, CancellationToken cancellationToken)
    {
        var pastDeadline = challenge.DeadlineUtc <= now;

        if (challenge.State == ChallengeState.Pending)
            return pastDeadline && await EndAsync(challenge.Id, ChallengeState.Pending, ChallengeState.Expired, now, cancellationToken);

        if (challenge.State != ChallengeState.Accepted)
            return false;

        var best = (int?)await ResultsInWindow(challenge).MaxAsync(r => (long?)r.Value, cancellationToken);

        ChallengeOutcome? outcome =
            best > challenge.BarPercent ? ChallengeOutcome.RecipientWon
            : !pastDeadline ? null
            : best is null ? ChallengeOutcome.None
            : best == challenge.BarPercent ? ChallengeOutcome.Draw
            : ChallengeOutcome.ChallengerWon;

        if (outcome is null)
        {
            // Still running: keep "your best so far" current for the list.
            if (best is { } soFar && soFar != challenge.RecipientBestPercent)
                await _dbContext.Challenges
                    .Where(c => c.Id == challenge.Id && c.State == ChallengeState.Accepted)
                    .ExecuteUpdateAsync(set => set.SetProperty(c => c.RecipientBestPercent, (int?)soFar), cancellationToken);

            return false;
        }

        // Accepted and never attempted: nobody won anything.
        var to = outcome == ChallengeOutcome.None ? ChallengeState.Expired : ChallengeState.Completed;

        var ended = await _dbContext.Challenges
            .Where(c => c.Id == challenge.Id && c.State == ChallengeState.Accepted)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.State, to)
                .SetProperty(c => c.Outcome, outcome.Value)
                .SetProperty(c => c.RecipientBestPercent, best)
                .SetProperty(c => c.EndedAtUtc, now), cancellationToken) > 0;

        if (!ended || to != ChallengeState.Completed)
            return ended;

        foreach (var player in new[] { challenge.ChallengerUserId, challenge.RecipientUserId })
        {
            var won = outcome == ChallengeOutcome.RecipientWon
                ? player == challenge.RecipientUserId
                : outcome == ChallengeOutcome.ChallengerWon && player == challenge.ChallengerUserId;

            _events.Stage(player, PlayerEventTypes.ChallengeCompleted, new
            {
                challengeId = challenge.Id,
                outcome = outcome.Value,
                barPercent = challenge.BarPercent,
                recipientBestPercent = best,
                youWon = won
            }, now.AddDays(ShownAfterEnding.TotalDays));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Challenge {ChallengeId} decided: {Outcome}.", challenge.Id, outcome);
        return true;
    }

    /// <summary>
    /// The recipient's graded attempts on the challenge's lesson and game, from acceptance to the
    /// deadline, flagged results excluded. (The sweep's query inlines the same predicate.)
    /// </summary>
    private IQueryable<GameResult> ResultsInWindow(Challenge c) =>
        _dbContext.GameResults.AsNoTracking()
            .Where(r => r.UserId == c.RecipientUserId
                        && r.Metric == LeaderboardMetrics.LessonBestPercent
                        && r.SourceType == GameResultSource.Attempt
                        && r.SourceId == c.LessonId
                        && r.GameId == c.GameId
                        && !r.IsFlagged
                        && r.OccurredAtUtc >= c.AcceptedAtUtc
                        && r.OccurredAtUtc <= c.DeadlineUtc);

    // ---- helpers -------------------------------------------------------------------------------

    private IQueryable<Challenge> Open() =>
        _dbContext.Challenges.AsNoTracking()
            .Where(c => c.State == ChallengeState.Pending || c.State == ChallengeState.Accepted);

    private Task<Challenge?> OpenBetweenAsync(Guid challenger, Guid recipient, Guid lessonId, CancellationToken cancellationToken) =>
        Open().FirstOrDefaultAsync(
            c => c.ChallengerUserId == challenger && c.RecipientUserId == recipient && c.LessonId == lessonId,
            cancellationToken);

    private Task<Challenge?> FindAsync(Guid challengeId, System.Linq.Expressions.Expression<Func<Challenge, bool>> party, CancellationToken cancellationToken) =>
        _dbContext.Challenges.AsNoTracking().Where(c => c.Id == challengeId).Where(party).FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> EndAsync(Guid challengeId, ChallengeState from, ChallengeState to, DateTime now, CancellationToken cancellationToken) =>
        await _dbContext.Challenges
            .Where(c => c.Id == challengeId && c.State == from)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.State, to)
                .SetProperty(c => c.EndedAtUtc, now), cancellationToken) > 0;

    /// <summary>
    /// A move that did not happen: idempotent success if the challenge is already where the caller
    /// wanted it, otherwise <c>CHALLENGE_NOT_OPEN</c> with the state it is actually in.
    /// </summary>
    private async Task<ServiceResult<ChallengeDto>> AnsweredAsync(Guid challengeId, Guid userId, ChallengeState wanted, CancellationToken cancellationToken)
    {
        var current = await ReloadAsync(challengeId, userId, cancellationToken);

        return current.Value!.State == wanted
            ? current
            : ServiceResult<ChallengeDto>.Failure(
                ApiErrors.ChallengeNotOpen,
                ServiceErrorKind.Conflict,
                $"The challenge is {current.Value.State}.",
                new Dictionary<string, object?> { ["state"] = current.Value.State.ToString() });
    }

    private async Task<ServiceResult<ChallengeDto>> ReloadAsync(Guid challengeId, Guid userId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.Challenges.AsNoTracking().FirstAsync(c => c.Id == challengeId, cancellationToken);
        return ServiceResult<ChallengeDto>.Success(await MapAsync(row, userId, cancellationToken));
    }

    private async Task<ChallengeDto> MapAsync(Challenge challenge, Guid viewer, CancellationToken cancellationToken)
    {
        var names = await _names.ResolveAsync([challenge.ChallengerUserId, challenge.RecipientUserId], cancellationToken);
        return ToDto(challenge, viewer, names, DateTime.UtcNow);
    }

    private static ChallengeDto ToDto(Challenge c, Guid viewer, IReadOnlyDictionary<Guid, string> names, DateTime now) => new()
    {
        Id = c.Id,
        ChallengerUserId = c.ChallengerUserId,
        ChallengerDisplayName = names.GetValueOrDefault(c.ChallengerUserId),
        RecipientUserId = c.RecipientUserId,
        RecipientDisplayName = names.GetValueOrDefault(c.RecipientUserId),
        YouAre = viewer == c.ChallengerUserId ? "challenger" : "recipient",
        GameId = c.GameId,
        LessonId = c.LessonId,
        BarPercent = c.BarPercent,
        RecipientBestPercent = c.RecipientBestPercent,
        State = c.State,
        Outcome = c.Outcome,
        CreatedAtUtc = Utc(c.CreatedAtUtc),
        AcceptedAtUtc = c.AcceptedAtUtc is { } accepted ? Utc(accepted) : null,
        DeadlineUtc = Utc(c.DeadlineUtc),
        EndedAtUtc = c.EndedAtUtc is { } ended ? Utc(ended) : null,
        ServerTimeUtc = now
    };

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private void Detach()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    private static ServiceResult<ChallengeDto> Failure(ApiErrorCode code, ServiceErrorKind kind, string message) =>
        ServiceResult<ChallengeDto>.Failure(code, kind, message);

    private static ServiceResult<ChallengeDto> NotFound(Guid challengeId) =>
        Failure(ApiErrors.ChallengeNotFound, ServiceErrorKind.NotFound, $"Challenge {challengeId} was not found.");

    private static ServiceResult<T> Relay<T>(ServiceResult failed) => new()
    {
        ErrorKind = failed.ErrorKind,
        Errors = failed.Errors,
        Error = failed.Error,
        Details = failed.Details
    };
}
