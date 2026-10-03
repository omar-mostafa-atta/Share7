using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Audit.Interfaces;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Feed;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Objectives.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Play.Models;
using Share7.Application.Progress.Interfaces;
using Share7.Application.Runs.Models;
using Share7.Domain.Audit;
using Share7.Domain.Feed;
using Share7.Domain.Leaderboards;
using Share7.Domain.Multiplayer;
using Share7.Domain.Organizations;
using Share7.Domain.Play;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Play;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Tournaments. See <see cref="ITournamentService"/>.
/// <para>
/// <b>Locking.</b> Everything that changes a tournament's shape — registering, starting, advancing a
/// round, withdrawing, an organiser's decision — takes the tournament's row first, so two of them
/// take turns. Pressing play is the exception: it locks only its own pairing, so a hundred pairings
/// opening their rooms at once do not queue behind each other. Play also holds a shared tournament
/// lock, so cancellation, withdrawal and advancement cannot invalidate its reads before it commits.
/// </para>
/// <para>
/// <b>Results are read, never pushed.</b> Nothing in the match-result path knows tournaments exist:
/// an advance reads the verdict of each pairing's room and settles the pairing from it.
/// </para>
/// </summary>
public sealed class TournamentService : ITournamentService
{
    /// <summary>Tournaments a sweeper pass starts, or advances, at most.</summary>
    private const int SweepBatch = 50;

    /// <summary>How long a finished tournament stays in the list players see.</summary>
    private static readonly TimeSpan FinishedListed = TimeSpan.FromDays(7);

    private readonly ApplicationDbContext _dbContext;
    private readonly MultiplayerSessionService _sessions;
    private readonly IPlaySelectionResolver _play;
    private readonly ISessionLessonMatcher _lessons;
    private readonly IUnlockService _unlocks;
    private readonly ILanguageService _language;
    private readonly IRosterNameResolver _names;
    private readonly IPlayerEventPublisher _events;
    private readonly IGameResultRecorder _results;
    private readonly IObjectiveProjector _objectives;
    private readonly EventPrizeAwardService _prizes;
    private readonly IAuditLog _audit;
    private readonly MultiplayerOptions _options;
    private readonly RunOptions _runOptions;
    private readonly ILogger<TournamentService> _logger;

    public TournamentService(
        ApplicationDbContext dbContext,
        MultiplayerSessionService sessions,
        IPlaySelectionResolver play,
        ISessionLessonMatcher lessons,
        IUnlockService unlocks,
        ILanguageService language,
        IRosterNameResolver names,
        IPlayerEventPublisher events,
        IGameResultRecorder results,
        IObjectiveProjector objectives,
        EventPrizeAwardService prizes,
        IAuditLog audit,
        IOptions<MultiplayerOptions> options,
        IOptions<RunOptions> runOptions,
        ILogger<TournamentService> logger)
    {
        _dbContext = dbContext;
        _sessions = sessions;
        _play = play;
        _lessons = lessons;
        _unlocks = unlocks;
        _language = language;
        _names = names;
        _events = events;
        _results = results;
        _objectives = objectives;
        _prizes = prizes;
        _audit = audit;
        _options = options.Value;
        _runOptions = runOptions.Value;
        _logger = logger;
    }

    // ---- reading -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<TournamentSummaryDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var listedSince = DateTime.UtcNow - FinishedListed;

        var myCohorts = await ActiveCohortsOfAsync(userId, cancellationToken);

        var tournaments = await _dbContext.Tournaments
            .AsNoTracking()
            .Where(t => ((t.State == TournamentState.Registration
                          || t.State == TournamentState.Running
                          || (t.State == TournamentState.Completed && t.CompletedAtUtc >= listedSince))
                         && (t.CohortId == null || myCohorts.Contains(t.CohortId.Value)))
                        || _dbContext.TournamentEntries.Any(e => e.TournamentId == t.Id && e.UserId == userId))
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        return await SummariesAsync(userId, tournaments, asAdmin: false, cancellationToken);
    }

    public async Task<IReadOnlyList<TournamentSummaryDto>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var tournaments = await _dbContext.Tournaments
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        return await SummariesAsync(Guid.Empty, tournaments, asAdmin: true, cancellationToken);
    }

    public async Task<ServiceResult<TournamentDto>> GetAsync(
        Guid userId, Guid tournamentId, bool asAdmin = false, CancellationToken cancellationToken = default)
    {
        var tournament = await _dbContext.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, cancellationToken);

        if (tournament is null || (!asAdmin && !await CanSeeAsync(userId, tournament, cancellationToken)))
            return NotFound();

        // Lazily, like a match result: the players looking at the bracket are what moves it on.
        if (tournament.State == TournamentState.Running && await HasDueWorkAsync(tournament, DateTime.UtcNow, cancellationToken))
            await AdvanceAndFinishAsync(tournamentId, cancellationToken);

        return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, asAdmin, cancellationToken));
    }

    // ---- entering ------------------------------------------------------------------------------

    public async Task<ServiceResult<TournamentDto>> RegisterAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await _dbContext.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, cancellationToken);

        if (tournament is null || !await CanSeeAsync(userId, tournament, cancellationToken))
            return NotFound();

        var existing = await _dbContext.TournamentEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TournamentId == tournamentId && e.UserId == userId, cancellationToken);

        // Entering twice is the first entry.
        if (existing?.State == TournamentEntryState.Entered)
            return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, false, cancellationToken));

        if (tournament.State != TournamentState.Registration)
            return Failure(ApiErrors.TournamentRegistrationClosed, ServiceErrorKind.Conflict, "Entries for this tournament are closed.");

        if (existing?.State == TournamentEntryState.Disqualified)
            return Failure(ApiErrors.TournamentNotEligible, ServiceErrorKind.Forbidden, "You can't enter this tournament.");

        if (await EligibilityAsync(userId, tournament, cancellationToken) is { } refusal)
            return refusal;

        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The cap, in the statement that takes the place: two players taking the last one cannot both get it.
        var taken = await _dbContext.Tournaments
            .Where(t => t.Id == tournamentId && t.State == TournamentState.Registration && t.EntrantCount < t.MaxEntrants)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.EntrantCount, t => t.EntrantCount + 1), cancellationToken);

        if (taken == 0)
        {
            await transaction.RollbackAsync(cancellationToken);

            var current = await _dbContext.Tournaments.AsNoTracking().FirstAsync(t => t.Id == tournamentId, cancellationToken);

            return current.State != TournamentState.Registration
                ? Failure(ApiErrors.TournamentRegistrationClosed, ServiceErrorKind.Conflict, "Entries for this tournament are closed.")
                : Failure(ApiErrors.TournamentFull, ServiceErrorKind.Conflict, "This tournament is full.");
        }

        if (existing is not null)
        {
            // Back in after withdrawing, before it started.
            var back = await _dbContext.TournamentEntries
                .Where(e => e.TournamentId == tournamentId && e.UserId == userId && e.State == TournamentEntryState.Withdrawn)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(e => e.State, TournamentEntryState.Entered)
                    .SetProperty(e => e.RegisteredAtUtc, now)
                    .SetProperty(e => e.LeftAtUtc, (DateTime?)null), cancellationToken);

            if (back == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, false, cancellationToken));
            }
        }
        else
        {
            _dbContext.TournamentEntries.Add(new TournamentEntry
            {
                TournamentId = tournamentId,
                UserId = userId,
                State = TournamentEntryState.Entered,
                RegisteredAtUtc = now
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                // The same player's other request entered first. Its place is the only one taken.
                await transaction.RollbackAsync(cancellationToken);
                Detach();
                return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, false, cancellationToken));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        MultiplayerMetrics.TournamentEntries.Add(1);

        return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, false, cancellationToken));
    }

    /// <summary>
    /// Why this player may not enter, or null. The same gate a match in the tournament's mode would
    /// apply — mode, entitlement, grade, the event's own rules — plus the class, and a lesson the
    /// player can actually open.
    /// </summary>
    private async Task<ServiceResult<TournamentDto>?> EligibilityAsync(Guid userId, Tournament tournament, CancellationToken cancellationToken)
    {
        if (tournament.CohortId is { } cohortId && !await IsLearnerInAsync(userId, cohortId, cancellationToken))
            return Failure(ApiErrors.TournamentNotEligible, ServiceErrorKind.Forbidden, "This tournament is for one class only.");

        var modeKey = await _dbContext.GameModes.AsNoTracking()
            .Where(m => m.Id == tournament.ModeId)
            .Select(m => m.ModeKey)
            .FirstOrDefaultAsync(cancellationToken);

        var selection = await _play.ResolveAsync(
            userId,
            new PlaySelectionRequest
            {
                GameId = tournament.GameId,
                ModeKey = modeKey,
                ContextKey = tournament.EventId is null ? null : PlayContextTokens.Event,
                EventId = tournament.EventId,
                PlayerCount = 2
            },
            cancellationToken);

        if (!selection.Succeeded)
            return new ServiceResult<TournamentDto>
            {
                ErrorKind = selection.ErrorKind,
                Errors = selection.Errors,
                Error = selection.Error,
                Details = selection.Details
            };

        if (tournament.LessonId is { } lessonId)
        {
            var unlocked = await _unlocks.GetUnlockedNodeIdsAsync(userId, tournament.GameId, cancellationToken);

            if (!unlocked.Contains(lessonId))
                return Failure(ApiErrors.TournamentLessonLocked, ServiceErrorKind.Conflict, "This tournament plays a lesson you haven't unlocked yet.");
        }
        else if (tournament.SubjectId is { } subjectId)
        {
            var langId = await _language.ResolveCurrentAsync(cancellationToken);
            var playable = await _lessons.EligibleLessonsAsync(userId, tournament.GameId, subjectId, langId, cancellationToken);

            if (playable.Count == 0)
                return Failure(ApiErrors.PlayNoSharedLesson, ServiceErrorKind.Conflict, "You have no unlocked lessons with questions in this subject yet.");
        }

        return null;
    }

    public async Task<ServiceResult<TournamentDto>> WithdrawAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await _dbContext.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, cancellationToken);

        if (tournament is null || !await CanSeeAsync(userId, tournament, cancellationToken))
            return NotFound();

        if (!await _dbContext.TournamentEntries.AnyAsync(
                e => e.TournamentId == tournamentId && e.UserId == userId && e.State == TournamentEntryState.Entered, cancellationToken))
            return Failure(ApiErrors.TournamentNotEntered, ServiceErrorKind.Conflict, "You're not in this tournament.");

        if (await RemoveEntrantAsync(tournamentId, userId, TournamentEntryState.Withdrawn, audit: null, cancellationToken) is { } refusal)
            return refusal;

        await AdvanceAndFinishAsync(tournamentId, cancellationToken);

        return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, false, cancellationToken));
    }

    /// <summary>
    /// Takes an entrant out — withdrawn, or disqualified — under the tournament's lock. Before the
    /// start their place is freed. After it, a pairing they still had to play goes to the opponent
    /// unless its room has already started, in which case the room's result decides it and they can
    /// no longer win it.
    /// </summary>
    private async Task<ServiceResult<TournamentDto>?> RemoveEntrantAsync(
        Guid tournamentId, Guid userId, TournamentEntryState to, AuditEntry? audit, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await LockAsync(tournamentId, cancellationToken);

        var tournament = await _dbContext.Tournaments.FirstAsync(t => t.Id == tournamentId, cancellationToken);
        var entry = await _dbContext.TournamentEntries.FirstOrDefaultAsync(e => e.TournamentId == tournamentId && e.UserId == userId, cancellationToken);

        if (entry is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(ApiErrors.TournamentNotEntered, ServiceErrorKind.Conflict, "Not in this tournament.");
        }

        if (tournament.State is TournamentState.Completed or TournamentState.Cancelled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(ApiErrors.TournamentInvalidState, ServiceErrorKind.Conflict, "This tournament is over.");
        }

        var wasIn = entry.State == TournamentEntryState.Entered;

        // Only someone still in can withdraw; anyone not already disqualified can be disqualified —
        // a player knocked out can still be found to have cheated their way to the round they reached.
        var applies = to == TournamentEntryState.Disqualified
            ? entry.State != TournamentEntryState.Disqualified
            : wasIn;

        if (!applies)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        entry.State = to;
        entry.LeftAtUtc = now;

        if (tournament.State == TournamentState.Registration)
        {
            if (wasIn)
                tournament.EntrantCount = Math.Max(0, tournament.EntrantCount - 1);
        }
        else if (wasIn)
        {
            if (tournament.Format == TournamentFormat.SingleElimination)
                entry.EliminatedInRound ??= tournament.CurrentRound;

            // Their pairing still to play goes to the opponent — unless its room is already playing.
            var match = await _dbContext.TournamentMatches.FirstOrDefaultAsync(
                m => m.TournamentId == tournamentId
                     && m.Round == tournament.CurrentRound
                     && m.State != TournamentMatchState.Completed
                     && (m.PlayerAUserId == userId || m.PlayerBUserId == userId),
                cancellationToken);

            if (match is not null && await CloseRoomIfUnstartedAsync(match, cancellationToken))
            {
                var entries = await _dbContext.TournamentEntries.Where(e => e.TournamentId == tournamentId).ToDictionaryAsync(e => e.UserId, cancellationToken);
                var names = await _names.ResolveAsync(entries.Keys.ToList(), cancellationToken);
                var pass = new Pass(tournament, entries, [match], names, now);

                var opponent = match.OpponentOf(userId);
                Settle(pass, match, opponent is { } other && IsIn(pass, other) ? other : null, TournamentOutcomes.Forfeit);
            }
        }

        if (audit is not null)
            _audit.Record(audit);

        tournament.AdvancedAtUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return null;
    }

    // ---- playing -------------------------------------------------------------------------------

    public async Task<ServiceResult<TournamentPlayDto>> PlayAsync(
        Guid userId,
        Guid tournamentId,
        Guid matchId,
        PlayTournamentMatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var tournament = await _dbContext.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, cancellationToken);

        if (tournament is null || !await CanSeeAsync(userId, tournament, cancellationToken))
            return PlayFailure(ApiErrors.TournamentNotFound, ServiceErrorKind.NotFound, "No such tournament.");

        // Settle anything due first, so a pairing whose deadline has just passed is not reopened.
        if (tournament.State == TournamentState.Running && await HasDueWorkAsync(tournament, DateTime.UtcNow, cancellationToken))
            await AdvanceAndFinishAsync(tournamentId, cancellationToken);

        var match = await _dbContext.TournamentMatches.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId && m.TournamentId == tournamentId, cancellationToken);

        if (match is null || !match.Involves(userId))
            return PlayFailure(ApiErrors.TournamentMatchNotFound, ServiceErrorKind.NotFound, "That pairing isn't yours.");

        if (!_options.EffectiveProtocolVersions.Contains(request.ProtocolVersion))
            return PlayFailure(ApiErrors.ProtocolVersionMismatch, ServiceErrorKind.Conflict, "This game version can't join the match.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Shared across independent pairings, exclusive against every organiser/advance mutation.
        // Re-read under this lock: cancellation can have committed after the preflight reads.
        await _dbContext.Database.ExecuteSqlRawAsync(
            "SELECT [Id] FROM [Tournaments] WITH (HOLDLOCK, ROWLOCK) WHERE [Id] = {0}",
            [tournamentId], cancellationToken);
        tournament = await _dbContext.Tournaments.AsNoTracking().FirstAsync(t => t.Id == tournamentId, cancellationToken);
        var now = DateTime.UtcNow;

        var stillIn = await _dbContext.TournamentEntries.AnyAsync(
            e => e.TournamentId == tournamentId && e.UserId == userId && e.State == TournamentEntryState.Entered, cancellationToken);

        if (tournament.State != TournamentState.Running || !stillIn || match.Round != tournament.CurrentRound
            || match.State == TournamentMatchState.Completed || match.DeadlineAtUtc <= now)
            return PlayFailure(ApiErrors.TournamentMatchClosed, ServiceErrorKind.Conflict, "This match is over, or its time ran out.");

        if (match.PlayerAUserId is not { } playerA || match.PlayerBUserId is not { } playerB)
            return PlayFailure(ApiErrors.TournamentMatchClosed, ServiceErrorKind.Conflict, "There's nobody to play in this pairing.");

        var isA = playerA == userId;

        if (await _dbContext.PlayerBlocks.AnyAsync(
                b => (b.UserId == playerA && b.BlockedUserId == playerB)
                     || (b.UserId == playerB && b.BlockedUserId == playerA), cancellationToken))
            return PlayFailure(ApiErrors.TournamentMatchClosed, ServiceErrorKind.Conflict, "This pairing is unavailable.");

        // Check in, guarded on the pairing still being playable — the row lock this takes is what a
        // deadline settling the same pairing meets.
        var checkedIn = await _dbContext.TournamentMatches
            .Where(m => m.Id == matchId
                        && m.GameNumber == match.GameNumber
                        && (m.State == TournamentMatchState.Ready || m.State == TournamentMatchState.Playing)
                        && m.DeadlineAtUtc > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(m => m.ACheckedInAtUtc, m => isA && m.ACheckedInAtUtc == null ? now : m.ACheckedInAtUtc)
                .SetProperty(m => m.BCheckedInAtUtc, m => !isA && m.BCheckedInAtUtc == null ? now : m.BCheckedInAtUtc), cancellationToken);

        if (checkedIn == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PlayFailure(ApiErrors.TournamentMatchClosed, ServiceErrorKind.Conflict, "This match is over, or its time ran out.");
        }

        // The other of the pair opened the room already: it is this caller's answer, to join once it is up.
        if (await _sessions.LiveTournamentRoomAsync(matchId, cancellationToken) is { } opened)
        {
            var currentRoom = await _sessions.DescribeAsync(opened, cancellationToken);
            if (currentRoom.ProtocolVersion != request.ProtocolVersion)
                return PlayFailure(ApiErrors.ProtocolVersionMismatch, ServiceErrorKind.Conflict, "This game version can't join the match.");
            await transaction.CommitAsync(cancellationToken);
            return await PlayAnswerAsync(userId, matchId, currentRoom, cancellationToken);
        }

        var modeKey = await _dbContext.GameModes.AsNoTracking()
            .Where(m => m.Id == tournament.ModeId)
            .Select(m => m.ModeKey)
            .FirstOrDefaultAsync(cancellationToken);

        var create = new CreateMultiplayerSessionRequest
        {
            GameId = tournament.GameId,
            ModeKey = modeKey,
            EventId = tournament.EventId,
            TransportSessionName = request.TransportSessionName,
            TransportRegion = request.TransportRegion,
            Visibility = SessionVisibility.Private,
            MaxPlayers = 2,

            // A tournament match moves no rating: the bracket chose the opponent, not the queue.
            IsRanked = false,
            ProtocolVersion = request.ProtocolVersion,
            CurriculumPath = PathOf(tournament),
            RequestId = request.RequestId
        };

        var created = await _sessions.CreateTournamentRoomAsync(userId, create, [playerA, playerB], matchId, cancellationToken);

        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            Detach();

            return new ServiceResult<TournamentPlayDto>
            {
                ErrorKind = created.ErrorKind,
                Errors = created.Errors,
                Error = created.Error,
                Details = created.Details
            };
        }

        var room = created.Value!;

        await _dbContext.TournamentMatches
            .Where(m => m.Id == matchId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(m => m.SessionId, room.Id)
                .SetProperty(m => m.State, TournamentMatchState.Playing), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Tournament {TournamentId} pairing {MatchId}: room {SessionId} opened by {UserId}.",
            tournamentId, matchId, room.Id, userId);

        return await PlayAnswerAsync(userId, matchId, room, cancellationToken);
    }

    private async Task<ServiceResult<TournamentPlayDto>> PlayAnswerAsync(
        Guid userId, Guid matchId, MultiplayerSessionDto room, CancellationToken cancellationToken)
    {
        var match = await _dbContext.TournamentMatches.AsNoTracking().FirstAsync(m => m.Id == matchId, cancellationToken);
        var names = await _names.ResolveAsync(new[] { match.PlayerAUserId, match.PlayerBUserId }.OfType<Guid>().ToList(), cancellationToken);

        return ServiceResult<TournamentPlayDto>.Success(new TournamentPlayDto
        {
            Match = MatchDto(match, names),
            Session = room,
            YouHost = room.HostUserId == userId
        });
    }

    // ---- organising ----------------------------------------------------------------------------

    public async Task<ServiceResult<TournamentDto>> CreateAsync(
        Guid userId, CreateTournamentRequest request, bool asAdmin, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var title = (request.Title ?? string.Empty).Trim();

        if (title.Length is 0 or > 80)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "A title of 1 to 80 characters is required.");

        if (request.EventId is not null && request.CohortId is not null)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "A tournament belongs to an event or to a class, not both.");

        // A teacher organises their own class's tournaments and nothing else. Events carry prizes,
        // and an open tournament reaches every player on the platform: both are an operator's call.
        if (!asAdmin && (request.CohortId is not { } teacherCohort || !await TeachesAsync(userId, teacherCohort, cancellationToken)))
            return Failure(ApiErrors.TournamentNotOrganiser, ServiceErrorKind.Forbidden, "Only a teacher of the class can create its tournament.");

        if (request.Format is not (TournamentFormat.SingleElimination or TournamentFormat.Swiss or TournamentFormat.RoundRobin))
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Unknown tournament format.");
        if (request.Format == TournamentFormat.RoundRobin && (request.MaxEntrants ?? 16) > 16)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Round-robin leagues support at most sixteen players.");

        var swissRounds = request.SwissRounds ?? 0;

        if (swissRounds < 0 || swissRounds > TournamentBrackets.MaxSwissRounds)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, $"A Swiss runs 1 to {TournamentBrackets.MaxSwissRounds} rounds.");

        var maxEntrants = request.MaxEntrants ?? Math.Min(request.Format == TournamentFormat.RoundRobin ? 16 : 64, _options.TournamentMaxEntrants);

        if (maxEntrants < 2 || maxEntrants > _options.TournamentMaxEntrants)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, $"A tournament takes 2 to {_options.TournamentMaxEntrants} players.");

        var matchMinutes = request.MatchMinutes ?? _options.TournamentMatchMinutes;

        if (matchMinutes < 2 || matchMinutes > 7 * 24 * 60)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Each match has 2 minutes to 7 days.");

        var startsAt = request.StartsAtUtc is { } requested ? DateTime.SpecifyKind(requested, DateTimeKind.Utc) : (DateTime?)null;

        if (startsAt is { } start && start <= now)
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "The start must be in the future.");

        var game = await _dbContext.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == request.GameId, cancellationToken);

        if (game is null)
            return Failure(ApiErrors.GameNotFound, ServiceErrorKind.NotFound, "No game has that id.");

        if (!game.SupportsMultiplayer || game.MaxPlayers < 2)
            return Failure(ApiErrors.GameNotMultiplayer, ServiceErrorKind.Conflict, "That game can't seat two players.");

        var mode = await _dbContext.GameModes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.ModeId, cancellationToken);

        if (mode is null || mode.GameId != game.Id)
            return Failure(ApiErrors.PlayModeUnknown, ServiceErrorKind.Validation, "Choose a mode of this game.");

        if (TournamentModeRefusal(mode) is { } unsuitable)
            return Failure(ApiErrors.TournamentModeUnsuitable, ServiceErrorKind.Conflict, unsuitable);

        if (request.EventId is { } eventId
            && await EventRefusalAsync(eventId, game.Id, mode.Id, startsAt, maxEntrants, request.Format, swissRounds, matchMinutes, cancellationToken) is { } eventProblem)
            return Failure(ApiErrors.TournamentEventUnsuitable, ServiceErrorKind.Conflict, eventProblem);

        if (request.CohortId is { } cohortId
            && !await _dbContext.Cohorts.AnyAsync(c => c.Id == cohortId && c.Status == CohortStatus.Active, cancellationToken))
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "That class isn't active.");

        var path = request.CurriculumPath;

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Title = title,
            GameId = game.Id,
            ModeId = mode.Id,
            EventId = request.EventId,
            CohortId = request.CohortId,
            Format = request.Format,
            SwissRounds = request.Format == TournamentFormat.Swiss ? swissRounds : 0,
            MaxEntrants = maxEntrants,
            MatchMinutes = matchMinutes,

            // One lesson for everyone, or a subject each pairing finds a shared lesson in.
            LessonId = path?.LessonId,
            SubjectId = path?.LessonId is null ? path?.SubjectId : null,
            State = TournamentState.Registration,
            StartsAtUtc = startsAt,
            RandomSeed = Random.Shared.Next(),
            CreatedByUserId = userId,
            CreatedAtUtc = now
        };

        _dbContext.Tournaments.Add(tournament);

        _audit.Record(new AuditEntry(
            AuditActions.TournamentCreated,
            AuditAreas.Competitions,
            "Created a tournament.",
            "tournament",
            tournament.Id.ToString(),
            new { format = tournament.Format.ToString(), tournament.EventId, tournament.CohortId, tournament.ModeId, tournament.MaxEntrants }));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (request.EventId is not null && IsUniqueViolation(exception))
        {
            Detach();
            return Failure(ApiErrors.TournamentEventUnsuitable, ServiceErrorKind.Conflict, "That event already has a tournament.");
        }

        _logger.LogInformation("Tournament {TournamentId} created by {UserId} ({Format}, event {EventId}, class {CohortId}).",
            tournament.Id, userId, tournament.Format, tournament.EventId, tournament.CohortId);

        return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournament.Id, asAdmin, cancellationToken));
    }

    /// <summary>
    /// Why a mode cannot host a tournament, or null. Pairings are two players against each other, and
    /// a match nobody can win cannot advance anyone — so a versus mode for two, with a win rule.
    /// </summary>
    internal static string? TournamentModeRefusal(GameMode mode) =>
        TournamentModeRefusal(mode.Topologies, mode.WinRule, mode.MinPlayers, mode.MaxPlayers);

    internal static string? TournamentModeRefusal(PlayTopologies topologies, MatchWinRule? rule, int minPlayers, int maxPlayers)
    {
        if (!topologies.HasFlag(PlayTopologies.Versus)
            || !PlayTopologyTokens.AllowsPlayerCount(topologies, 2)
            || minPlayers > 2
            || maxPlayers < 2)
            return "A tournament needs a mode played versus, by two.";

        if (rule is null)
            return "A tournament needs a mode with a way to win: give the mode a win rule first.";

        return null;
    }

    /// <summary>
    /// Why an event cannot host this tournament, or null. Its prize table is paid by the bracket, so it
    /// hosts one tournament at most; its per-entry limits would stop a player mid-bracket; and every
    /// round has to fit inside its window, because a match cannot be played once the event closes.
    /// </summary>
    private async Task<string?> EventRefusalAsync(
        Guid eventId, Guid gameId, Guid modeId, DateTime? startsAt, int maxEntrants, TournamentFormat format,
        int swissRounds, int matchMinutes, CancellationToken cancellationToken)
    {
        var playEvent = await _dbContext.PlayEvents.AsNoTracking()
            .Include(e => e.Cycle)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (playEvent is null || !playEvent.IsActive || playEvent.CancelledAtUtc is not null)
            return "That event isn't running.";

        if (playEvent.GameId != gameId || playEvent.ModeId != modeId)
            return "A tournament plays its event's own game and mode.";

        if (await _dbContext.EventPrizeTiers.AnyAsync(t => t.EventId == eventId && t.Kind == EventPrizeKind.RealWorld, cancellationToken))
        {
            var rule = (await _dbContext.GameModes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modeId, cancellationToken))?.WinRule;
            if (rule is null || rule.Trust != MatchMetricTrust.Verified)
                return "Real-prize tournaments require entirely server-verified win criteria.";
        }

        if (playEvent.MaxEntriesPerDay is not null || playEvent.MaxEntriesTotal is not null)
            return "Remove the event's entry limits first: the bracket decides how many matches each player plays.";

        if (playEvent.Cycle is not { } cycle || cycle.State is not (LeaderboardCycleState.Scheduled or LeaderboardCycleState.Open))
            return "That event has already closed.";

        if (await _dbContext.Tournaments.AnyAsync(t => t.EventId == eventId && t.State != TournamentState.Cancelled, cancellationToken))
            return "That event already has a tournament.";

        if (startsAt is { } start)
        {
            if (start < cycle.StartsAtUtc || start >= cycle.EndsAtUtc)
                return "Start the tournament inside the event's window.";

            var rounds = RoundsFor(format, maxEntrants, swissRounds);

            if (start.AddMinutes((double)rounds * matchMinutes) > cycle.EndsAtUtc)
                return $"A full field needs {rounds} rounds of {matchMinutes} minutes, which runs past the event's end.";
        }

        return null;
    }

    private static int RoundsFor(TournamentFormat format, int entrants, int swissRounds) =>
        format == TournamentFormat.RoundRobin ? TournamentBrackets.RoundRobinRounds(entrants) : format == TournamentFormat.Swiss
            ? TournamentBrackets.SwissRoundsFor(entrants, swissRounds)
            : TournamentBrackets.EliminationRounds(entrants);

    public async Task<ServiceResult<TournamentDto>> StartAsync(Guid userId, Guid tournamentId, bool asAdmin, CancellationToken cancellationToken = default)
    {
        if (await OrganiserCheckAsync(userId, tournamentId, asAdmin, cancellationToken) is { } refusal)
            return refusal;

        var tournament = await _dbContext.Tournaments.AsNoTracking().FirstAsync(t => t.Id == tournamentId, cancellationToken);

        if (tournament.State != TournamentState.Registration)
            return tournament.State == TournamentState.Running
                ? ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, asAdmin, cancellationToken))
                : Failure(ApiErrors.TournamentInvalidState, ServiceErrorKind.Conflict, "Only a tournament taking entries can be started.");

        // Started by hand inside an event: every round still has to fit before the event closes.
        if (tournament.EventId is { } eventId)
        {
            var endsAt = await _dbContext.PlayEvents.AsNoTracking()
                .Where(e => e.Id == eventId)
                .Select(e => e.Cycle!.EndsAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var rounds = RoundsFor(tournament.Format, Math.Max(2, tournament.EntrantCount), tournament.SwissRounds);

            if (DateTime.UtcNow.AddMinutes((double)rounds * tournament.MatchMinutes) > endsAt)
                return Failure(ApiErrors.TournamentEventUnsuitable, ServiceErrorKind.Conflict,
                    $"{rounds} rounds of {tournament.MatchMinutes} minutes would run past the event's end.");
        }

        await StartCoreAsync(tournamentId, DateTime.UtcNow, userId, cancellationToken);
        await AdvanceAndFinishAsync(tournamentId, cancellationToken);

        return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, asAdmin, cancellationToken));
    }

    public async Task<ServiceResult<TournamentDto>> CancelAsync(
        Guid userId, Guid tournamentId, string? reason, bool asAdmin, CancellationToken cancellationToken = default)
    {
        if (await OrganiserCheckAsync(userId, tournamentId, asAdmin, cancellationToken) is { } refusal)
            return refusal;

        var said = string.IsNullOrWhiteSpace(reason) ? "cancelled_by_organiser" : Clip(reason, 200);

        var audit = new AuditEntry(
            AuditActions.TournamentCancelled, AuditAreas.Competitions, "Cancelled a tournament.", "tournament", tournamentId.ToString(),
            new { reason = said });

        if (!await CancelCoreAsync(tournamentId, said, DateTime.UtcNow, audit, cancellationToken))
            return Failure(ApiErrors.TournamentInvalidState, ServiceErrorKind.Conflict, "This tournament is already over.");

        return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, asAdmin, cancellationToken));
    }

    public async Task<ServiceResult<TournamentDto>> DecideMatchAsync(
        Guid userId,
        Guid tournamentId,
        Guid matchId,
        DecideTournamentMatchRequest request,
        bool asAdmin,
        CancellationToken cancellationToken = default)
    {
        if (await OrganiserCheckAsync(userId, tournamentId, asAdmin, cancellationToken) is { } refusal)
            return refusal;

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Say why — the reason is kept with the decision.");

        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await LockAsync(tournamentId, cancellationToken);

        var tournament = await _dbContext.Tournaments.FirstAsync(t => t.Id == tournamentId, cancellationToken);
        var match = await _dbContext.TournamentMatches.FirstOrDefaultAsync(m => m.Id == matchId && m.TournamentId == tournamentId, cancellationToken);

        if (match is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(ApiErrors.TournamentMatchNotFound, ServiceErrorKind.NotFound, "No such pairing.");
        }

        // A decided pairing stands: the next round was built on it. Disqualifying is the remedy after.
        if (tournament.State != TournamentState.Running || match.State == TournamentMatchState.Completed || match.Round != tournament.CurrentRound)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(ApiErrors.TournamentMatchClosed, ServiceErrorKind.Conflict, "Only a pairing still being played can be settled by hand.");
        }

        if (!request.Replay && (request.WinnerUserId is not { } chosen || !match.Involves(chosen)))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Choose one of the pair, or a replay.");
        }

        // A room still waiting for its players is closed either way; one already playing is left to
        // finish, and its result no longer counts.
        if (match.SessionId is { } roomId)
            await _sessions.CloseUnstartedAsync(roomId, SessionClosedReason.AdminClosed, cancellationToken);

        var entries = await _dbContext.TournamentEntries.Where(e => e.TournamentId == tournamentId).ToDictionaryAsync(e => e.UserId, cancellationToken);
        var names = await _names.ResolveAsync(entries.Keys.ToList(), cancellationToken);
        var pass = new Pass(tournament, entries, [match], names, now);

        if (request.Replay)
            Replay(pass, match);
        else
            Settle(pass, match, request.WinnerUserId, TournamentOutcomes.Organiser);

        _audit.Record(new AuditEntry(
            AuditActions.TournamentMatchDecided,
            AuditAreas.Competitions,
            request.Replay ? "Ordered a tournament pairing replayed." : "Settled a tournament pairing by hand.",
            "tournament_match",
            matchId.ToString(),
            new { tournamentId, winnerUserId = request.Replay ? null : request.WinnerUserId, replay = request.Replay, reason = Clip(request.Reason, 200) }));

        tournament.AdvancedAtUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await AdvanceAndFinishAsync(tournamentId, cancellationToken);

        return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, asAdmin, cancellationToken));
    }

    public async Task<ServiceResult<TournamentDto>> DisqualifyAsync(
        Guid userId, Guid tournamentId, Guid entrantUserId, string reason, bool asAdmin, CancellationToken cancellationToken = default)
    {
        if (await OrganiserCheckAsync(userId, tournamentId, asAdmin, cancellationToken) is { } refusal)
            return refusal;

        if (string.IsNullOrWhiteSpace(reason))
            return Failure(ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Say why — the reason is kept with the decision.");

        var audit = new AuditEntry(
            AuditActions.TournamentEntrantDisqualified,
            AuditAreas.Competitions,
            "Disqualified a tournament entrant.",
            "tournament",
            tournamentId.ToString(),
            new { entrantUserId, reason = Clip(reason, 200) });

        if (await RemoveEntrantAsync(tournamentId, entrantUserId, TournamentEntryState.Disqualified, audit, cancellationToken) is { } failed)
            return failed;

        await AdvanceAndFinishAsync(tournamentId, cancellationToken);

        return ServiceResult<TournamentDto>.Success(await DescribeAsync(userId, tournamentId, asAdmin, cancellationToken));
    }

    private async Task<ServiceResult<TournamentDto>?> OrganiserCheckAsync(
        Guid userId, Guid tournamentId, bool asAdmin, CancellationToken cancellationToken)
    {
        var tournament = await _dbContext.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tournamentId, cancellationToken);

        if (tournament is null)
            return NotFound();

        if (asAdmin)
            return null;

        if (tournament.CohortId is { } cohortId && await TeachesAsync(userId, cohortId, cancellationToken))
            return null;

        // Someone who cannot see it is told it does not exist; a player of it is told it is not theirs to run.
        return await CanSeeAsync(userId, tournament, cancellationToken)
            ? Failure(ApiErrors.TournamentNotOrganiser, ServiceErrorKind.Forbidden, "Only its organiser can do that.")
            : NotFound();
    }

    // ---- the sweeper ---------------------------------------------------------------------------

    public async Task<int> AdvanceDueAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var moved = 0;

        var due = await _dbContext.Tournaments.AsNoTracking()
            .Where(t => t.State == TournamentState.Registration && t.StartsAtUtc != null && t.StartsAtUtc <= now)
            .OrderBy(t => t.StartsAtUtc)
            .Select(t => t.Id)
            .Take(SweepBatch)
            .ToListAsync(cancellationToken);

        foreach (var id in due)
        {
            try
            {
                if (await StartCoreAsync(id, now, actingUserId: null, cancellationToken))
                    moved++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Detach();
                _logger.LogError(exception, "Could not start tournament {TournamentId}; will retry.", id);
            }
        }

        var running = await _dbContext.Tournaments.AsNoTracking()
            .Where(t => t.State == TournamentState.Running)
            .OrderBy(t => t.AdvancedAtUtc)
            .Select(t => t.Id)
            .Take(SweepBatch)
            .ToListAsync(cancellationToken);

        foreach (var id in running)
        {
            try
            {
                if (await AdvanceAndFinishAsync(id, cancellationToken))
                    moved++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Detach();
                _logger.LogError(exception, "Could not advance tournament {TournamentId}; will retry.", id);
            }
        }

        // A payout that failed after the tournament completed is retried until it lands.
        var unpaid = await _dbContext.Tournaments.AsNoTracking()
            .Where(t => t.State == TournamentState.Completed && t.EventId != null && t.PrizesAwardedAtUtc == null)
            .Select(t => t.Id)
            .Take(SweepBatch)
            .ToListAsync(cancellationToken);

        foreach (var id in unpaid)
            await PayPrizesAsync(id, cancellationToken);

        return moved;
    }

    // ---- starting ------------------------------------------------------------------------------

    /// <summary>
    /// Seeds the field and writes the first round, under the tournament's lock. Fewer than two
    /// entrants cancels it — a tournament of one is not a win. Returns whether it moved.
    /// </summary>
    private async Task<bool> StartCoreAsync(Guid tournamentId, DateTime now, Guid? actingUserId, CancellationToken cancellationToken)
    {
        string? cancelBecause;

        await using (var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockAsync(tournamentId, cancellationToken);

            var tournament = await _dbContext.Tournaments.FirstAsync(t => t.Id == tournamentId, cancellationToken);

            if (tournament.State != TournamentState.Registration)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            var entries = await _dbContext.TournamentEntries
                .Where(e => e.TournamentId == tournamentId)
                .ToDictionaryAsync(e => e.UserId, cancellationToken);

            var field = entries.Values.Where(e => e.State == TournamentEntryState.Entered).ToList();

            var eventClosed = tournament.EventId is { } eventId
                              && !await _dbContext.PlayEvents.AnyAsync(
                                  e => e.Id == eventId && e.IsActive && e.CancelledAtUtc == null
                                       && e.Cycle!.State == LeaderboardCycleState.Open, cancellationToken);

            cancelBecause = eventClosed ? "event_closed" : field.Count < 2 ? "too_few_players" : null;

            if (cancelBecause is null)
            {
                // Seeds from the mode's hidden rating where players have one, so the strongest meet
                // last; everyone else — and every tie — in an order fixed by the tournament's own seed.
                var ids = field.Select(e => e.UserId).ToList();

                var ordinals = await _dbContext.PlayerRatings.AsNoTracking()
                    .Where(r => r.ModeId == tournament.ModeId && ids.Contains(r.UserId))
                    .ToDictionaryAsync(r => r.UserId, r => r.Mu - 3 * r.Sigma, cancellationToken);

                var shuffle = new Random(tournament.RandomSeed);
                var tieBreak = ids.OrderBy(id => id).ToDictionary(id => id, _ => shuffle.Next());

                var seeded = ids
                    .OrderByDescending(id => ordinals.GetValueOrDefault(id, RatingModel.Initial.Ordinal))
                    .ThenBy(id => tieBreak[id])
                    .ToList();

                for (var i = 0; i < seeded.Count; i++)
                    entries[seeded[i]].Seed = i + 1;

                tournament.State = TournamentState.Running;
                tournament.StartedAtUtc = now;
                tournament.AdvancedAtUtc = now;
                tournament.CurrentRound = 1;
                tournament.RoundCount = RoundsFor(tournament.Format, seeded.Count, tournament.SwissRounds);

                var names = await _names.ResolveAsync(entries.Keys.ToList(), cancellationToken);
                var pass = new Pass(tournament, entries, [], names, now)
                {
                    Blocked = await BlockedPairsAsync(ids, cancellationToken)
                };

                var pairings = tournament.Format == TournamentFormat.SingleElimination
                    ? TournamentBrackets.SeparateBlocked(TournamentBrackets.EliminationFirstRound(seeded), pass.IsBlocked, id => entries[id].Seed)
                    : tournament.Format == TournamentFormat.RoundRobin
                    ? TournamentBrackets.RoundRobinRound(seeded, 1, _ => true, pass.IsBlocked)
                    : TournamentBrackets.SwissRound(
                        seeded.Select(id => new SwissPlayer(id, 0, entries[id].Seed, false)).ToList(), 1, (_, _) => false, pass.IsBlocked);

                WriteRound(pass, 1, pairings);

                if (actingUserId is not null)
                    _audit.Record(new AuditEntry(
                        AuditActions.TournamentStarted, AuditAreas.Competitions, "Started a tournament.", "tournament", tournamentId.ToString(),
                        new { entrants = seeded.Count, tournament.RoundCount }));

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                MultiplayerMetrics.TournamentsStarted.Add(1, MultiplayerMetrics.Tag("format", tournament.Format.ToString()));
                _logger.LogInformation("Tournament {TournamentId} started: {Entrants} entrants, {Rounds} rounds.",
                    tournamentId, seeded.Count, tournament.RoundCount);

                return true;
            }

            await transaction.RollbackAsync(cancellationToken);
        }

        Detach();
        await CancelCoreAsync(tournamentId, cancelBecause, now, audit: null, cancellationToken);
        return true;
    }

    /// <summary>Calls a tournament off under its lock, closes rooms still waiting, and tells everyone in it.</summary>
    private async Task<bool> CancelCoreAsync(Guid tournamentId, string reason, DateTime now, AuditEntry? audit, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await LockAsync(tournamentId, cancellationToken);

        var tournament = await _dbContext.Tournaments.FirstAsync(t => t.Id == tournamentId, cancellationToken);

        if (tournament.State is not (TournamentState.Registration or TournamentState.Running))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        tournament.State = TournamentState.Cancelled;
        tournament.CancelledAtUtc = now;
        tournament.CancelReason = reason;
        tournament.AdvancedAtUtc = now;

        var rooms = await _dbContext.TournamentMatches
            .Where(m => m.TournamentId == tournamentId && m.State != TournamentMatchState.Completed && m.SessionId != null)
            .Select(m => m.SessionId!.Value)
            .ToListAsync(cancellationToken);

        foreach (var room in rooms)
            await _sessions.CloseUnstartedAsync(room, SessionClosedReason.AdminClosed, cancellationToken);

        var players = await _dbContext.TournamentEntries
            .Where(e => e.TournamentId == tournamentId && e.State == TournamentEntryState.Entered)
            .Select(e => e.UserId)
            .ToListAsync(cancellationToken);

        foreach (var player in players)
            _events.Stage(player, PlayerEventTypes.TournamentCancelled, new { tournamentId, reason }, now.AddDays(1));

        if (audit is not null)
            _audit.Record(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Tournament {TournamentId} cancelled ({Reason}).", tournamentId, reason);
        return true;
    }

    // ---- advancing -----------------------------------------------------------------------------

    /// <summary>
    /// Everything one advance needs in hand: the tournament and its entries (tracked), the round's
    /// pairings (tracked), names for the feed, who may not meet whom, and the clock.
    /// </summary>
    private sealed class Pass(
        Tournament tournament,
        Dictionary<Guid, TournamentEntry> entries,
        List<TournamentMatch> round,
        IReadOnlyDictionary<Guid, string> names,
        DateTime now)
    {
        public Tournament Tournament { get; } = tournament;
        public Dictionary<Guid, TournamentEntry> Entries { get; } = entries;
        public List<TournamentMatch> Round { get; set; } = round;
        public IReadOnlyDictionary<Guid, string> Names { get; } = names;
        public DateTime Now { get; } = now;
        public HashSet<(Guid, Guid)> Blocked { get; init; } = [];

        public bool IsBlocked(Guid a, Guid b) => Blocked.Contains((a, b));
    }

    /// <summary>Advances, then — if that completed the tournament — does what follows outside its transaction.</summary>
    private async Task<bool> AdvanceAndFinishAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        var (moved, completed) = await AdvanceCoreAsync(tournamentId, DateTime.UtcNow, cancellationToken);

        if (completed)
            await AfterCompletionAsync(tournamentId, cancellationToken);

        return moved;
    }

    /// <summary>
    /// Settles every pairing of the current round that can be settled — a verdict in, a room that
    /// closed unplayed, a deadline passed — and, when the round is complete, writes the next one or
    /// finishes the tournament. Under the tournament's lock; idempotent.
    /// </summary>
    private async Task<(bool Moved, bool Completed)> AdvanceCoreAsync(Guid tournamentId, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await LockAsync(tournamentId, cancellationToken);

        var tournament = await _dbContext.Tournaments.FirstAsync(t => t.Id == tournamentId, cancellationToken);

        if (tournament.State != TournamentState.Running)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (false, false);
        }

        var entries = await _dbContext.TournamentEntries
            .Where(e => e.TournamentId == tournamentId)
            .ToDictionaryAsync(e => e.UserId, cancellationToken);

        var round = await _dbContext.TournamentMatches
            .Where(m => m.TournamentId == tournamentId && m.Round == tournament.CurrentRound)
            .OrderBy(m => m.Position)
            .ToListAsync(cancellationToken);

        var names = await _names.ResolveAsync(entries.Keys.ToList(), cancellationToken);
        var pass = new Pass(tournament, entries, round, names, now)
        {
            Blocked = await BlockedPairsAsync(entries.Keys.ToList(), cancellationToken)
        };

        // What the open pairings' rooms say.
        var roomIds = round.Where(m => m.State != TournamentMatchState.Completed && m.SessionId is not null)
            .Select(m => m.SessionId!.Value)
            .ToList();

        var rooms = await _dbContext.MultiplayerSessions.AsNoTracking()
            .Where(s => roomIds.Contains(s.Id))
            .Select(s => new Room(s.Id, s.State, s.StartedAtUtc, s.EndedAtUtc))
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var verdicts = await _dbContext.MatchResults.AsNoTracking()
            .Include(r => r.Placements)
            .Where(r => roomIds.Contains(r.SessionId))
            .ToDictionaryAsync(r => r.SessionId, cancellationToken);

        var moved = false;

        foreach (var match in round.Where(m => m.State != TournamentMatchState.Completed).ToList())
            moved |= await ResolveAsync(pass, match, rooms, verdicts, cancellationToken);

        var completed = false;

        // A finished round writes the next — which may itself finish at once, all byes — until a
        // round has something left to play or the tournament is over.
        while (pass.Round.All(m => m.State == TournamentMatchState.Completed))
        {
            moved = true;

            if (IsFinished(pass))
            {
                await CompleteAsync(pass, cancellationToken);
                completed = true;
                break;
            }

            await NextRoundAsync(pass, cancellationToken);
        }

        if (!moved)
        {
            // Rotate the bounded sweeper's queue even when this tournament is waiting. Otherwise
            // fifty idle tournaments stay oldest forever and starve every tournament behind them.
            tournament.AdvancedAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (false, false);
        }

        tournament.AdvancedAtUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return (true, completed);
    }

    private sealed record Room(Guid Id, MultiplayerSessionState State, DateTime? StartedAtUtc, DateTime? EndedAtUtc);

    /// <summary>Settles one open pairing if anything decides it. Returns whether it changed.</summary>
    private async Task<bool> ResolveAsync(
        Pass pass,
        TournamentMatch match,
        IReadOnlyDictionary<Guid, Room> rooms,
        IReadOnlyDictionary<Guid, MatchResult> verdicts,
        CancellationToken cancellationToken)
    {
        var a = match.PlayerAUserId;
        var b = match.PlayerBUserId;

        var room = match.SessionId is { } roomId ? rooms.GetValueOrDefault(roomId) : null;
        var started = room?.StartedAtUtc is not null;

        // A block made since seeding is as binding as one that existed in the draw. Never disclose
        // the block; use the same neutral not-played outcome as an unavoidable blocked draw.
        if (!started && a is { } blockedA && b is { } blockedB && pass.IsBlocked(blockedA, blockedB))
        {
            if (!await CloseRoomIfUnstartedAsync(match, cancellationToken))
                return false;
            Settle(pass, match, pass.Tournament.Format == TournamentFormat.SingleElimination ? BetterSeed(pass, match) : null,
                TournamentOutcomes.NotPlayed);
            return true;
        }

        // Someone who left the tournament cannot play on. Unless their room is already playing — then
        // its result decides, and they cannot be the one to go through. An open pairing with an empty
        // side is one whose player's account was erased: they have left too.
        var someoneLeft = !(a is { } playerA && IsIn(pass, playerA)) || !(b is { } playerB && IsIn(pass, playerB));

        if (someoneLeft && !started)
        {
            if (room is not null)
                await _sessions.CloseUnstartedAsync(room.Id, SessionClosedReason.AdminClosed, cancellationToken);

            var remaining = new[] { a, b }.OfType<Guid>().Where(id => IsIn(pass, id)).Select(id => (Guid?)id).FirstOrDefault();
            Settle(pass, match, remaining, TournamentOutcomes.Forfeit);
            return true;
        }

        if (room is not null)
        {
            if (verdicts.TryGetValue(room.Id, out var verdict))
            {
                ApplyVerdict(pass, match, verdict);
                return true;
            }

            if (started)
            {
                // Playing, or ended and waiting for its verdict. A verdict that can never come — the
                // match outlived what a run may take to settle — is a match with no result.
                var giveUpAt = room.StartedAtUtc!.Value
                    .AddMinutes(_runOptions.RunLifetimeMinutes)
                    .AddSeconds(_options.MatchResultGraceSeconds * 2);

                if (pass.Now < giveUpAt)
                    return false;

                NoClearWinner(pass, match, played: true, level: false);
                return true;
            }

            // The room ended without its match ever starting. The pairing opens again until its deadline.
            if (room.State.IsTerminal())
            {
                match.SessionId = null;
                match.State = TournamentMatchState.Ready;

                if (pass.Now < match.DeadlineAtUtc)
                    return true;
            }
        }

        if (match.DeadlineAtUtc is not { } deadline || pass.Now < deadline)
            return false;

        // Time is up and nothing was played. Whoever pressed play showed up.
        if (match.SessionId is { } waiting)
        {
            await _sessions.CloseUnstartedAsync(waiting, SessionClosedReason.DeadlinePassed, cancellationToken);
            match.SessionId = null;
        }

        var cameA = match.ACheckedInAtUtc is not null;
        var cameB = match.BCheckedInAtUtc is not null;

        // Both came and still never started: nothing was played, so nothing replays.
        if (cameA && cameB)
            NoClearWinner(pass, match, played: false, level: false);
        else if (cameA || cameB)
            Settle(pass, match, cameA ? a : b, TournamentOutcomes.Walkover);
        else
            Settle(pass, match, null, TournamentOutcomes.NoShow);

        return true;
    }

    /// <summary>
    /// A pairing's verdict. One clean winner goes through. Anything else — level, nobody clean, a
    /// voided result — is a draw in a Swiss, and in a knockout a replay, then the higher seed.
    /// </summary>
    private void ApplyVerdict(Pass pass, TournamentMatch match, MatchResult verdict)
    {
        var pair = new[] { match.PlayerAUserId, match.PlayerBUserId }.OfType<Guid>().ToHashSet();
        var lines = verdict.Placements.Where(p => pair.Contains(p.UserId)).ToList();

        match.Flagged |= lines.Any(p => p.Flagged);

        var winners = verdict.State == MatchResultState.Decided
            ? lines.Where(p => p.IsWinner).Select(p => p.UserId).ToList()
            : [];

        if (winners.Count == 1)
        {
            var winner = winners[0];

            // A winner who has since left the tournament cannot go through; their opponent does.
            if (!IsIn(pass, winner))
            {
                var other = match.OpponentOf(winner);
                Settle(pass, match, other is { } o && IsIn(pass, o) ? o : null, TournamentOutcomes.Forfeit);
                return;
            }

            Settle(pass, match, winner, TournamentOutcomes.Played);
            return;
        }

        if (verdict.State == MatchResultState.Void)
        {
            Replay(pass, match);
            return;
        }

        var level = lines.Count == 2 && lines.All(p => !p.Forfeited && !p.Flagged);
        NoClearWinner(pass, match, played: true, level: level);
    }

    /// <summary>
    /// No winner from play. A Swiss scores it — a draw when both played clean to a level result, or
    /// both came and nothing was played; a loss each when a game was played and nobody finished it
    /// clean. A knockout must produce one: a replay of a played game, up to the limit, then the
    /// higher seed.
    /// </summary>
    private void NoClearWinner(Pass pass, TournamentMatch match, bool played, bool level)
    {
        if (pass.Tournament.Format != TournamentFormat.SingleElimination)
        {
            Settle(pass, match, null, !played || level ? TournamentOutcomes.Draw : TournamentOutcomes.NoResult);
            return;
        }

        if (played && match.GameNumber < TournamentBrackets.MaxGamesPerMatch)
        {
            Replay(pass, match);
            return;
        }

        Settle(pass, match, BetterSeed(pass, match), TournamentOutcomes.Seed);
    }

    /// <summary>A fresh game for the same pairing: a new room, a new deadline, nobody checked in.</summary>
    private void Replay(Pass pass, TournamentMatch match)
    {
        match.GameNumber++;
        match.SessionId = null;
        match.State = TournamentMatchState.Ready;
        match.ACheckedInAtUtc = null;
        match.BCheckedInAtUtc = null;
        match.ReadyAtUtc = pass.Now;
        match.DeadlineAtUtc = pass.Now.AddMinutes(pass.Tournament.MatchMinutes);

        AnnounceReady(pass, match, replay: true);
    }

    private static Guid? BetterSeed(Pass pass, TournamentMatch match) =>
        new[] { match.PlayerAUserId, match.PlayerBUserId }
            .OfType<Guid>()
            .Where(id => IsIn(pass, id))
            .OrderBy(id => pass.Entries[id].Seed)
            .Select(id => (Guid?)id)
            .FirstOrDefault();

    /// <summary>
    /// Settles a pairing and scores it: a win is two half-points in a Swiss, a draw one; a knockout
    /// loser goes out in this round; a no-show counts toward two-in-a-row withdrawal (Swiss).
    /// </summary>
    private void Settle(Pass pass, TournamentMatch match, Guid? winner, string outcome)
    {
        var now = pass.Now;
        var knockout = pass.Tournament.Format == TournamentFormat.SingleElimination;

        match.State = TournamentMatchState.Completed;
        match.WinnerUserId = winner;
        match.Outcome = outcome;
        match.CompletedAtUtc = now;

        var draw = winner is null && !knockout && outcome is TournamentOutcomes.Draw or TournamentOutcomes.NotPlayed;

        foreach (var player in new[] { match.PlayerAUserId, match.PlayerBUserId }.OfType<Guid>())
        {
            if (!pass.Entries.TryGetValue(player, out var entry))
                continue;

            var showed = outcome switch
            {
                TournamentOutcomes.NoShow => false,
                TournamentOutcomes.Walkover => player == winner,
                _ => true
            };

            entry.MissedMatches = showed ? 0 : entry.MissedMatches + 1;

            string result;

            if (outcome == TournamentOutcomes.Bye && player == winner)
            {
                entry.Byes++;
                entry.Points += 2;
                result = "bye";
            }
            else if (player == winner)
            {
                entry.Wins++;
                entry.Points += 2;
                result = "won";
            }
            else if (draw)
            {
                entry.Draws++;
                entry.Points += 1;
                result = "draw";
            }
            else
            {
                entry.Losses++;
                result = "lost";
            }

            var eliminated = false;

            if (entry.State == TournamentEntryState.Entered)
            {
                if (knockout && player != winner)
                {
                    entry.State = TournamentEntryState.Eliminated;
                    entry.EliminatedInRound = match.Round;
                    entry.LeftAtUtc = now;
                    eliminated = true;
                }
                else if (!knockout && entry.MissedMatches >= 2)
                {
                    // Two missed in a row: out, rather than handed to opponent after opponent as a free win.
                    entry.State = TournamentEntryState.Withdrawn;
                    entry.LeftAtUtc = now;
                    eliminated = true;
                }
            }

            _events.Stage(player, PlayerEventTypes.TournamentMatchDecided, new
            {
                tournamentId = pass.Tournament.Id,
                matchId = match.Id,
                round = match.Round,
                outcome,
                result,
                eliminated
            }, now.AddDays(1));
        }

        MultiplayerMetrics.TournamentMatches.Add(1, MultiplayerMetrics.Tag("outcome", outcome));
    }

    private static bool IsIn(Pass pass, Guid userId) =>
        pass.Entries.TryGetValue(userId, out var entry) && entry.State == TournamentEntryState.Entered;

    /// <summary>Whether the tournament is over now that its current round is complete.</summary>
    private static bool IsFinished(Pass pass) =>
        pass.Tournament.CurrentRound >= pass.Tournament.RoundCount

        // A Swiss with fewer than two left has nobody to pair.
        || (pass.Tournament.Format != TournamentFormat.SingleElimination
            && pass.Entries.Values.Count(e => e.State == TournamentEntryState.Entered) < 2);

    /// <summary>Writes the next round from the one just completed.</summary>
    private async Task NextRoundAsync(Pass pass, CancellationToken cancellationToken)
    {
        var tournament = pass.Tournament;
        var next = tournament.CurrentRound + 1;
        IReadOnlyList<TournamentPairing> pairings;

        if (tournament.Format == TournamentFormat.SingleElimination)
        {
            // The winners of 2p and 2p+1 meet at p. A winner who has since left is an empty slot.
            var winners = pass.Round
                .OrderBy(m => m.Position)
                .Select(m => m.WinnerUserId is { } w && IsIn(pass, w) ? (Guid?)w : null)
                .ToList();

            pairings = TournamentBrackets.SeparateBlocked(
                TournamentBrackets.EliminationNextRound(winners, id => pass.Entries[id].Seed),
                pass.IsBlocked,
                id => pass.Entries[id].Seed);
        }
        else if (tournament.Format == TournamentFormat.RoundRobin)
        {
            pairings = TournamentBrackets.RoundRobinRound(pass.Entries.Values.OrderBy(e => e.Seed).Select(e => e.UserId).ToArray(),
                next, id => IsIn(pass, id), pass.IsBlocked);
        }
        else
        {
            var met = (await PairsMetAsync(pass, cancellationToken))
                .SelectMany(p => new[] { (p.A, p.B), (p.B, p.A) })
                .ToHashSet();

            var players = pass.Entries.Values
                .Where(e => e.State == TournamentEntryState.Entered)
                .Select(e => new SwissPlayer(e.UserId, e.Points, e.Seed, e.Byes > 0))
                .ToList();

            pairings = TournamentBrackets.SwissRound(players, next, (x, y) => met.Contains((x, y)), pass.IsBlocked);
        }

        tournament.CurrentRound = next;
        WriteRound(pass, next, pairings);
    }

    /// <summary>Every pair ever drawn together in this tournament — saved rounds, and the round in hand.</summary>
    private async Task<List<(Guid A, Guid B)>> PairsMetAsync(Pass pass, CancellationToken cancellationToken)
    {
        var saved = await _dbContext.TournamentMatches.AsNoTracking()
            .Where(m => m.TournamentId == pass.Tournament.Id && m.PlayerAUserId != null && m.PlayerBUserId != null)
            .Select(m => new { A = m.PlayerAUserId!.Value, B = m.PlayerBUserId!.Value })
            .ToListAsync(cancellationToken);

        return saved
            .Select(p => (p.A, p.B))
            .Concat(pass.Round
                .Where(m => m.PlayerAUserId is not null && m.PlayerBUserId is not null)
                .Select(m => (m.PlayerAUserId!.Value, m.PlayerBUserId!.Value)))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Writes a round's pairings. Byes, empty branches and pairs who may not meet are settled on the
    /// spot; everyone else is told their match is ready, with the deadline it has to be played by.
    /// </summary>
    private void WriteRound(Pass pass, int round, IReadOnlyList<TournamentPairing> pairings)
    {
        var matches = new List<TournamentMatch>(pairings.Count);

        for (var position = 0; position < pairings.Count; position++)
        {
            var pairing = pairings[position];

            var match = new TournamentMatch
            {
                Id = Guid.NewGuid(),
                TournamentId = pass.Tournament.Id,
                Round = round,
                Position = position,
                PlayerAUserId = pairing.A,
                PlayerBUserId = pairing.B,
                State = TournamentMatchState.Ready,
                GameNumber = 1,
                ReadyAtUtc = pass.Now,
                DeadlineAtUtc = pass.Now.AddMinutes(pass.Tournament.MatchMinutes)
            };

            _dbContext.TournamentMatches.Add(match);
            matches.Add(match);

            if (pairing.A is null && pairing.B is null)
                Settle(pass, match, null, TournamentOutcomes.Empty);
            else if (pairing.B is null)
                Settle(pass, match, pairing.A, TournamentOutcomes.Bye);
            else if (pairing.NotPlayed)
                Settle(pass, match,
                    pass.Tournament.Format == TournamentFormat.SingleElimination ? BetterSeed(pass, match) : null,
                    TournamentOutcomes.NotPlayed);
            else
                AnnounceReady(pass, match, replay: false);
        }

        pass.Round = matches;
    }

    private void AnnounceReady(Pass pass, TournamentMatch match, bool replay)
    {
        foreach (var (player, opponent) in new[] { (match.PlayerAUserId, match.PlayerBUserId), (match.PlayerBUserId, match.PlayerAUserId) })
        {
            if (player is not { } recipient)
                continue;

            _events.Stage(recipient, PlayerEventTypes.TournamentMatchReady, new
            {
                tournamentId = pass.Tournament.Id,
                matchId = match.Id,
                round = match.Round,
                gameNumber = match.GameNumber,
                opponentUserId = opponent,
                opponentName = opponent is { } o ? pass.Names.GetValueOrDefault(o) : null,
                deadlineAtUtc = match.DeadlineAtUtc,
                replay
            }, match.DeadlineAtUtc);
        }
    }

    // ---- completing ----------------------------------------------------------------------------

    /// <summary>
    /// Final placements, the game-results stream (so quests and season tracks can count tournaments),
    /// and the news, in the advance's transaction. Prizes and quest progress follow after it commits.
    /// </summary>
    private async Task CompleteAsync(Pass pass, CancellationToken cancellationToken)
    {
        var tournament = pass.Tournament;
        var now = pass.Now;
        var started = pass.Entries.Values.Where(e => e.Seed > 0).ToList();

        if (tournament.Format == TournamentFormat.SingleElimination)
        {
            var final = pass.Round.FirstOrDefault(m => m.Position == 0);
            var champion = final?.WinnerUserId is { } w && IsIn(pass, w) ? w : (Guid?)null;

            foreach (var entry in started)
            {
                entry.Placement = entry.State == TournamentEntryState.Disqualified ? null
                    : entry.UserId == champion ? 1
                    : TournamentBrackets.EliminationPlacement(tournament.RoundCount, entry.EliminatedInRound ?? tournament.RoundCount);
            }
        }
        else
        {
            var pairs = await PairsMetAsync(pass, cancellationToken);

            // Buchholz: the points of everyone you were drawn against.
            int Buchholz(Guid user) => pairs
                .Where(p => p.A == user || p.B == user)
                .Sum(p => pass.Entries.TryGetValue(p.A == user ? p.B : p.A, out var opponent) ? opponent.Points : 0);

            var records = started
                .Where(e => e.State != TournamentEntryState.Disqualified)
                .Select(e => new SwissRecord(e.UserId, e.Points, Buchholz(e.UserId), e.Wins, e.Seed));

            foreach (var (userId, placement) in TournamentBrackets.SwissPlacements(records))
                pass.Entries[userId].Placement = placement;
        }

        tournament.State = TournamentState.Completed;
        tournament.CompletedAtUtc = now;

        foreach (var entry in started)
        {
            _events.Stage(entry.UserId, PlayerEventTypes.TournamentCompleted, new
            {
                tournamentId = tournament.Id,
                placement = entry.Placement,
                champion = entry.Placement == 1
            }, now.AddDays(7));
        }

        await EmitAsync(pass, cancellationToken);

        MultiplayerMetrics.TournamentsCompleted.Add(1, MultiplayerMetrics.Tag("format", tournament.Format.ToString()));
        _logger.LogInformation("Tournament {TournamentId} completed after {Rounds} round(s).", tournament.Id, tournament.CurrentRound);
    }

    /// <summary>
    /// <c>TOURNAMENTS_PLAYED</c> for everyone who started it and was not disqualified, and
    /// <c>TOURNAMENTS_WON</c> for first place — so season tracks and quests count tournaments without
    /// knowing they exist (MultiplayerPlatform.md §6.1: producers never call consumers).
    /// </summary>
    private async Task EmitAsync(Pass pass, CancellationToken cancellationToken)
    {
        var tournament = pass.Tournament;
        var context = tournament.EventId is null ? PlayContextKind.FreePlay : PlayContextKind.Event;
        var play = await _play.DescribeAsync(tournament.ModeId, context, tournament.EventId, cancellationToken);

        if (play.Policy == PlaySettlementPolicy.Nothing)
            return;

        var counted = pass.Entries.Values
            .Where(e => e.Seed > 0 && e.State != TournamentEntryState.Disqualified)
            .ToList();

        var ids = counted.Select(e => e.UserId).ToList();

        var grades = await _dbContext.StudentProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => (Guid?)p.GradeId, cancellationToken);

        foreach (var entry in counted)
        {
            var metrics = new List<GameResultDraft> { new(LeaderboardMetrics.TournamentsPlayed, 1) };

            if (entry.Placement == 1)
                metrics.Add(new GameResultDraft(LeaderboardMetrics.TournamentsWon, 1));

            await _results.RecordAsync(new GameResultContext
            {
                UserId = entry.UserId,
                GameId = tournament.GameId,
                SourceId = tournament.Id,
                OccurredAtUtc = pass.Now,
                GradeId = grades.GetValueOrDefault(entry.UserId),

                // Once per player per tournament, by the stream's own unique index.
                RequestId = $"tournament:{tournament.Id:N}",
                SourceType = GameResultSource.Tournament,
                ModeId = tournament.ModeId,
                Context = context,
                EventId = tournament.EventId,
                CountsForRanking = play.Policy.Ranks,
                Metrics = metrics
            }, cancellationToken);
        }
    }

    /// <summary>After a completion commits: quest progress on the spot, and the event's prizes.</summary>
    private async Task AfterCompletionAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        var players = await _dbContext.TournamentEntries.AsNoTracking()
            .Where(e => e.TournamentId == tournamentId && e.Seed > 0 && e.State != TournamentEntryState.Disqualified)
            .Select(e => e.UserId)
            .ToListAsync(cancellationToken);

        foreach (var player in players)
        {
            try
            {
                await _objectives.ProjectForUserAsync(player, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The results are in the stream; the next thing this player does folds them in.
                Detach();
                _logger.LogWarning(exception, "Quest progress for {UserId} after tournament {TournamentId} will catch up later.", player, tournamentId);
            }
        }

        await PayPrizesAsync(tournamentId, cancellationToken);
    }

    /// <summary>Pays an event tournament's prize table from its final placements, once.</summary>
    private async Task PayPrizesAsync(Guid tournamentId, CancellationToken cancellationToken)
    {
        var due = await _dbContext.Tournaments.AsNoTracking()
            .AnyAsync(t => t.Id == tournamentId && t.State == TournamentState.Completed && t.EventId != null && t.PrizesAwardedAtUtc == null, cancellationToken);

        if (!due)
            return;

        try
        {
            await _prizes.AwardTournamentAsync(tournamentId, cancellationToken);

            await _dbContext.Tournaments
                .Where(t => t.Id == tournamentId)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.PrizesAwardedAtUtc, DateTime.UtcNow), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Every award is claimed by a unique index before anything is paid, so the retry the
            // sweeper makes finishes the job without paying anyone twice.
            Detach();
            _logger.LogError(exception, "Prizes for tournament {TournamentId} did not all land; the sweeper will retry.", tournamentId);
        }
    }

    // ---- describing ----------------------------------------------------------------------------

    private async Task<IReadOnlyList<TournamentSummaryDto>> SummariesAsync(
        Guid userId, List<Tournament> tournaments, bool asAdmin, CancellationToken cancellationToken)
    {
        if (tournaments.Count == 0)
            return [];

        var ids = tournaments.Select(t => t.Id).ToList();

        var mine = await _dbContext.TournamentEntries.AsNoTracking()
            .Where(e => ids.Contains(e.TournamentId) && e.UserId == userId)
            .ToDictionaryAsync(e => e.TournamentId, cancellationToken);

        var modeIds = tournaments.Select(t => t.ModeId).Distinct().ToList();
        var modeKeys = await _dbContext.GameModes.AsNoTracking()
            .Where(m => modeIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.ModeKey, cancellationToken);

        HashSet<Guid> taught = asAdmin ? [] : await TaughtCohortsAsync(userId, cancellationToken);

        return tournaments
            .Select(t => Summary(new TournamentSummaryDto(), t, modeKeys, mine.GetValueOrDefault(t.Id),
                asAdmin || (t.CohortId is { } c && taught.Contains(c))))
            .ToList();
    }

    private static T Summary<T>(T dto, Tournament t, IReadOnlyDictionary<Guid, string> modeKeys, TournamentEntry? mine, bool canManage)
        where T : TournamentSummaryDto
    {
        dto.Id = t.Id;
        dto.Title = t.Title;
        dto.GameId = t.GameId;
        dto.ModeId = t.ModeId;
        dto.ModeKey = modeKeys.GetValueOrDefault(t.ModeId);
        dto.Scope = t.EventId is not null ? "event" : t.CohortId is not null ? "classroom" : "open";
        dto.EventId = t.EventId;
        dto.CohortId = t.CohortId;
        dto.Format = t.Format;
        dto.State = t.State;
        dto.EntrantCount = t.EntrantCount;
        dto.MaxEntrants = t.MaxEntrants;
        dto.CurrentRound = t.CurrentRound;
        dto.RoundCount = t.RoundCount;
        dto.MatchMinutes = t.MatchMinutes;
        dto.CurriculumPath = PathOf(t);
        dto.StartsAtUtc = Utc(t.StartsAtUtc);
        dto.StartedAtUtc = Utc(t.StartedAtUtc);
        dto.CompletedAtUtc = Utc(t.CompletedAtUtc);
        dto.CreatedAtUtc = DateTime.SpecifyKind(t.CreatedAtUtc, DateTimeKind.Utc);
        dto.MyState = mine?.State;
        dto.MyPlacement = mine?.Placement;
        dto.CanManage = canManage;
        return dto;
    }

    private async Task<TournamentDto> DescribeAsync(Guid userId, Guid tournamentId, bool asAdmin, CancellationToken cancellationToken)
    {
        var tournament = await _dbContext.Tournaments.AsNoTracking().FirstAsync(t => t.Id == tournamentId, cancellationToken);

        // Everyone who is or was in it — but not someone who withdrew before it began.
        var entries = await _dbContext.TournamentEntries.AsNoTracking()
            .Where(e => e.TournamentId == tournamentId && (e.State != TournamentEntryState.Withdrawn || e.Seed > 0))
            .ToListAsync(cancellationToken);

        var matches = await _dbContext.TournamentMatches.AsNoTracking()
            .Where(m => m.TournamentId == tournamentId)
            .OrderBy(m => m.Round)
            .ThenBy(m => m.Position)
            .ToListAsync(cancellationToken);

        var names = await _names.ResolveAsync(entries.Select(e => e.UserId).ToList(), cancellationToken);

        var modeKeys = await _dbContext.GameModes.AsNoTracking()
            .Where(m => m.Id == tournament.ModeId)
            .ToDictionaryAsync(m => m.Id, m => m.ModeKey, cancellationToken);

        var mine = entries.FirstOrDefault(e => e.UserId == userId);
        var canManage = asAdmin || (tournament.CohortId is { } cohortId && await TeachesAsync(userId, cohortId, cancellationToken));

        var dto = Summary(new TournamentDto(), tournament, modeKeys, mine, canManage);

        dto.CancelReason = tournament.CancelReason;
        dto.ServerTimeUtc = DateTime.UtcNow;

        dto.Entrants = entries
            .OrderBy(e => e.Placement ?? int.MaxValue)
            .ThenByDescending(e => e.Points)
            .ThenBy(e => e.Seed == 0 ? int.MaxValue : e.Seed)
            .ThenBy(e => e.RegisteredAtUtc)
            .Select(e => new TournamentEntrantDto
            {
                UserId = e.UserId,
                DisplayName = names.GetValueOrDefault(e.UserId),
                Seed = e.Seed,
                State = e.State,
                Points = e.Points / 2.0,
                Wins = e.Wins,
                Losses = e.Losses,
                Draws = e.Draws,
                Byes = e.Byes,
                Placement = e.Placement
            })
            .ToList();

        dto.Rounds = matches
            .GroupBy(m => m.Round)
            .Select(g => new TournamentRoundDto { Round = g.Key, Matches = g.Select(m => MatchDto(m, names)).ToList() })
            .ToList();

        var myMatch = tournament.State == TournamentState.Running
            ? matches.FirstOrDefault(m => m.Round == tournament.CurrentRound && m.State != TournamentMatchState.Completed && m.Involves(userId))
            : null;

        dto.MyMatch = myMatch is null ? null : MatchDto(myMatch, names);

        return dto;
    }

    private static TournamentMatchDto MatchDto(TournamentMatch m, IReadOnlyDictionary<Guid, string> names) => new()
    {
        Id = m.Id,
        Round = m.Round,
        Position = m.Position,
        PlayerAUserId = m.PlayerAUserId,
        PlayerAName = m.PlayerAUserId is { } a ? names.GetValueOrDefault(a) : null,
        PlayerBUserId = m.PlayerBUserId,
        PlayerBName = m.PlayerBUserId is { } b ? names.GetValueOrDefault(b) : null,
        State = m.State,
        GameNumber = m.GameNumber,
        SessionId = m.State == TournamentMatchState.Completed ? null : m.SessionId,
        PlayerACheckedIn = m.ACheckedInAtUtc is not null,
        PlayerBCheckedIn = m.BCheckedInAtUtc is not null,
        DeadlineAtUtc = Utc(m.DeadlineAtUtc),
        WinnerUserId = m.WinnerUserId,
        Outcome = m.Outcome,
        Flagged = m.Flagged
    };

    // ---- who may do what -----------------------------------------------------------------------

    /// <summary>Anyone may see an open or event tournament; a classroom one only its class, and its entrants.</summary>
    private async Task<bool> CanSeeAsync(Guid userId, Tournament tournament, CancellationToken cancellationToken)
    {
        if (tournament.CohortId is not { } cohortId)
            return true;

        return await _dbContext.CohortMemberships.AnyAsync(m => m.CohortId == cohortId && m.UserId == userId && m.LeftAtUtc == null, cancellationToken)
               || await _dbContext.TournamentEntries.AnyAsync(e => e.TournamentId == tournament.Id && e.UserId == userId, cancellationToken);
    }

    private async Task<List<Guid>> ActiveCohortsOfAsync(Guid userId, CancellationToken cancellationToken) =>
        await _dbContext.CohortMemberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.LeftAtUtc == null)
            .Select(m => m.CohortId)
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>A learner in an active class of an active organization — the same test as "classmates".</summary>
    private Task<bool> IsLearnerInAsync(Guid userId, Guid cohortId, CancellationToken cancellationToken) =>
        (from membership in _dbContext.CohortMemberships
         where membership.CohortId == cohortId && membership.UserId == userId
               && membership.LeftAtUtc == null && membership.Role == CohortRole.Learner
         join cohort in _dbContext.Cohorts on membership.CohortId equals cohort.Id
         where cohort.Status == CohortStatus.Active
         join org in _dbContext.Organizations on cohort.OrgId equals org.Id
         where org.Status == OrganizationStatus.Active
         select membership.Id).AnyAsync(cancellationToken);

    /// <summary>
    /// A teacher, or an assistant, of an active class of an active organization. The relationship is
    /// the authorization — a role claim says nothing about whose class this is.
    /// </summary>
    private Task<bool> TeachesAsync(Guid userId, Guid cohortId, CancellationToken cancellationToken) =>
        TeachingQuery(userId).AnyAsync(id => id == cohortId, cancellationToken);

    private async Task<HashSet<Guid>> TaughtCohortsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await TeachingQuery(userId).ToListAsync(cancellationToken)).ToHashSet();

    private IQueryable<Guid> TeachingQuery(Guid userId) =>
        from membership in _dbContext.CohortMemberships
        where membership.UserId == userId && membership.LeftAtUtc == null
              && (membership.Role == CohortRole.Teacher || membership.Role == CohortRole.Assistant)
        join cohort in _dbContext.Cohorts on membership.CohortId equals cohort.Id
        where cohort.Status == CohortStatus.Active
        join org in _dbContext.Organizations on cohort.OrgId equals org.Id
        where org.Status == OrganizationStatus.Active
        select cohort.Id;

    // ---- helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Whether a running tournament has anything to settle: a verdict in, a room ended unplayed, a
    /// deadline passed. Checked without the lock, so the players watching a quiet bracket cost a read.
    /// </summary>
    private Task<bool> HasDueWorkAsync(Tournament tournament, DateTime now, CancellationToken cancellationToken) =>
        _dbContext.TournamentMatches.AsNoTracking()
            .Where(m => m.TournamentId == tournament.Id && m.Round == tournament.CurrentRound)
            .AnyAsync(m => m.State != TournamentMatchState.Completed
                           && (m.DeadlineAtUtc <= now
                               || _dbContext.PlayerBlocks.Any(b =>
                                   (b.UserId == m.PlayerAUserId && b.BlockedUserId == m.PlayerBUserId)
                                   || (b.UserId == m.PlayerBUserId && b.BlockedUserId == m.PlayerAUserId))
                               || (m.SessionId != null
                                   && (_dbContext.MatchResults.Any(r => r.SessionId == m.SessionId)
                                       || _dbContext.MultiplayerSessions.Any(s => s.Id == m.SessionId
                                                                                  && (s.State == MultiplayerSessionState.Closed
                                                                                      || s.State == MultiplayerSessionState.Failed
                                                                                      || s.State == MultiplayerSessionState.Abandoned))))),
                cancellationToken);

    /// <summary>Takes the tournament's row for this transaction. Every change to a tournament's shape starts here.</summary>
    private Task LockAsync(Guid tournamentId, CancellationToken cancellationToken) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE [Tournaments] SET [AdvancedAtUtc] = [AdvancedAtUtc] WHERE [Id] = {0}",
            [tournamentId],
            cancellationToken);

    /// <summary>Closes the pairing's room if it has not started. False when it is playing — its result decides.</summary>
    private async Task<bool> CloseRoomIfUnstartedAsync(TournamentMatch match, CancellationToken cancellationToken)
    {
        if (match.SessionId is not { } roomId)
            return true;

        var started = await _dbContext.MultiplayerSessions.AsNoTracking()
            .AnyAsync(s => s.Id == roomId && s.StartedAtUtc != null, cancellationToken);

        if (started)
            return false;

        if (!await _sessions.CloseUnstartedAsync(roomId, SessionClosedReason.AdminClosed, cancellationToken)
            && await _dbContext.MultiplayerSessions.AsNoTracking().AnyAsync(
                s => s.Id == roomId && s.StartedAtUtc != null, cancellationToken))
            return false;
        match.SessionId = null;
        return true;
    }

    /// <summary>Pairs in the field where either has blocked the other — never seated together.</summary>
    private async Task<HashSet<(Guid, Guid)>> BlockedPairsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await _dbContext.PlayerBlocks.AsNoTracking()
            .Where(b => ids.Contains(b.UserId) && ids.Contains(b.BlockedUserId))
            .Select(b => new { b.UserId, b.BlockedUserId })
            .ToListAsync(cancellationToken))
        .SelectMany(b => new[] { (b.UserId, b.BlockedUserId), (b.BlockedUserId, b.UserId) })
        .ToHashSet();

    private static CurriculumPathDto? PathOf(Tournament t) =>
        t.LessonId is { } lessonId ? new CurriculumPathDto { LessonId = lessonId }
        : t.SubjectId is { } subjectId ? new CurriculumPathDto { SubjectId = subjectId }
        : null;

    private static DateTime? Utc(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private static string Clip(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private void Detach()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    private static ServiceResult<TournamentDto> NotFound() =>
        Failure(ApiErrors.TournamentNotFound, ServiceErrorKind.NotFound, "No such tournament.");

    private static ServiceResult<TournamentDto> Failure(ApiErrorCode code, ServiceErrorKind kind, string message) =>
        ServiceResult<TournamentDto>.Failure(code, kind, message);

    private static ServiceResult<TournamentPlayDto> PlayFailure(ApiErrorCode code, ServiceErrorKind kind, string message) =>
        ServiceResult<TournamentPlayDto>.Failure(code, kind, message);
}
