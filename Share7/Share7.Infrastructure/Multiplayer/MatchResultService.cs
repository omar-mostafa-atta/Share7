using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Feed;
using Share7.Domain.Feed;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Objectives.Interfaces;
using Share7.Application.Play.Interfaces;
using Share7.Application.Runs.Models;
using Share7.Domain.Economy;
using Share7.Domain.Leaderboards;
using Share7.Domain.Multiplayer;
using Share7.Domain.Play;
using Share7.Domain.Runs;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Runs;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Decides matches. See <see cref="IMatchResultService"/>.
/// <para>
/// <b>Who took part</b> is whoever held a seat at the moment the match started — not whoever is
/// seated now, which after a close is nobody. <b>What they scored</b> is read from their own settled
/// runs and their graded attempts for the match's lesson, never from anything a client says about the
/// match itself. <b>Who won</b> is the mode's rule applied to that, with two rules of its own on top:
/// a player who reported nothing is placed after everyone who did, and a player who reported past
/// what is physically possible is placed after every player who did not.
/// </para>
/// </summary>
public sealed class MatchResultService : IMatchResultService
{
    internal const string AllReported = "all_reported";
    internal const string Deadline = "deadline";

    /// <summary>Matches considered per sweep pass. The rest wait for the next pass.</summary>
    private const int BatchSize = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _dbContext;
    private readonly IRosterNameResolver _names;
    private readonly IPlaySelectionResolver _play;
    private readonly IGameResultRecorder _results;
    private readonly IObjectiveProjector _objectives;
    private readonly IPlayerEventPublisher _events;
    private readonly IRatingService _ratings;
    private readonly SweeperWarmup _warmup;
    private readonly MultiplayerOptions _options;
    private readonly RunOptions _runOptions;
    private readonly ILogger<MatchResultService> _logger;

    public MatchResultService(
        ApplicationDbContext dbContext,
        IRosterNameResolver names,
        IPlaySelectionResolver play,
        IGameResultRecorder results,
        IObjectiveProjector objectives,
        IPlayerEventPublisher events,
        IRatingService ratings,
        SweeperWarmup warmup,
        IOptions<MultiplayerOptions> options,
        IOptions<RunOptions> runOptions,
        ILogger<MatchResultService> logger)
    {
        _dbContext = dbContext;
        _names = names;
        _play = play;
        _results = results;
        _objectives = objectives;
        _events = events;
        _ratings = ratings;
        _warmup = warmup;
        _options = options.Value;
        _runOptions = runOptions.Value;
        _logger = logger;
    }

    // ---- reading -------------------------------------------------------------------------------

    public async Task<ServiceResult<MatchResultDto>> GetAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        // The session's own read rule: anyone who has held a seat, except someone the host removed.
        var canSee = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .AnyAsync(p => p.SessionId == sessionId
                           && p.UserId == userId
                           && !_dbContext.MultiplayerSessionBans.Any(b => b.SessionId == sessionId && b.UserId == userId),
                cancellationToken);

        if (!canSee)
            return NotFound(sessionId);

        if (await ReadAsync(sessionId, cancellationToken) is { } decided)
            return ServiceResult<MatchResultDto>.Success(await WithRankedAsync(await MapAsync(decided, cancellationToken), userId, cancellationToken));

        var session = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
            return NotFound(sessionId);

        var now = DateTime.UtcNow;
        var outcome = await TryDecideAsync(session, now, AllowDeadline(now), cancellationToken);

        return ServiceResult<MatchResultDto>.Success(
            outcome.Result is { } result
                ? await WithRankedAsync(await MapAsync(result, cancellationToken), userId, cancellationToken)
                : outcome.Pending!);
    }

    /// <summary>The caller's own ranked outcome, added to a rated match's result for them alone.</summary>
    private async Task<MatchResultDto> WithRankedAsync(MatchResultDto dto, Guid userId, CancellationToken cancellationToken)
    {
        dto.Ranked = await _ratings.ResultForAsync(dto.SessionId, userId, cancellationToken);
        return dto;
    }

    public async Task<int> DecideDueAsync(bool allowDeadline, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Only matches young enough to still gain a result: a run cannot settle after its own
        // lifetime, so a match older than that plus the grace period has nothing left to wait for.
        // Anything older was either decided in time or is left undecided, rather than handing out
        // forfeits for matches played before results existed.
        var horizon = now.AddMinutes(-_runOptions.RunLifetimeMinutes).AddSeconds(-_options.MatchResultGraceSeconds);

        // A running match with no settled run yet cannot be decided by anything, so it is not even
        // loaded. That keeps a pass cheap while many matches are in progress.
        var candidates = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .Where(s => s.StartedAtUtc != null
                        && s.StartedAtUtc >= horizon
                        && !_dbContext.MatchResults.Any(r => r.SessionId == s.Id)
                        && (s.State == MultiplayerSessionState.Closed
                            || s.State == MultiplayerSessionState.Abandoned
                            || s.State == MultiplayerSessionState.Failed
                            || _dbContext.Runs.Any(r => r.SessionId == s.Id && r.State == RunState.Settled)
                            || _dbContext.MatchAttemptScores.Any(a => a.SessionId == s.Id)))
            .OrderBy(s => s.StartedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var decided = 0;

        foreach (var session in candidates)
        {
            try
            {
                var outcome = await TryDecideAsync(session, now, allowDeadline, cancellationToken);

                if (outcome.DecidedHere)
                    decided++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One match that cannot be decided must not stop the rest from being decided. Its
                // transaction has rolled back; it is simply tried again on the next pass.
                Detach();
                _logger.LogError(exception, "Could not decide match {SessionId}; will retry.", session.Id);
            }
        }

        return decided;
    }

    public async Task<IReadOnlyList<MatchMetricOptionDto>> MetricOptionsAsync(
        Guid? gameId,
        CancellationToken cancellationToken = default)
    {
        var options = MatchMetrics.FixedTokens
            .Select(metric => Option(metric, metric == MatchMetrics.DurationMs ? MatchRankOrder.LowerWins : MatchRankOrder.HigherWins))
            .ToList();

        // Every count the game — or the platform, for every game — has a row for, plus the kinds the
        // platform names itself. A kind a mini-game reports but nobody has priced yet can still be
        // typed as signal:<kind>; this list is what a form offers, not what is allowed.
        var priced = await _dbContext.SignalValuations
            .AsNoTracking()
            .Where(v => v.Enabled && (v.GameId == null || v.GameId == gameId))
            .Select(v => v.SignalKind)
            .Distinct()
            .ToListAsync(cancellationToken);

        var kinds = priced
            .Concat([SignalKinds.Coin, SignalKinds.NearMiss, SignalKinds.DistanceM])
            .Select(kind => MatchMetrics.Normalise(MatchMetrics.SignalPrefix + kind))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal);

        options.AddRange(kinds.Select(metric => Option(metric, MatchRankOrder.HigherWins)));

        return options;
    }

    private static MatchMetricOptionDto Option(string metric, MatchRankOrder suggested) => new()
    {
        Metric = metric,
        Trust = TrustToken(MatchMetrics.TrustOf(metric)),
        Source = MatchMetrics.IsAnswerMetric(metric) ? "answers" : "run",
        SuggestedOrder = MatchWinRule.OrderToken(suggested)
    };

    // ---- deciding ------------------------------------------------------------------------------

    /// <summary>What an attempt to decide produced: the verdict, or where the match stands.</summary>
    private sealed record Outcome(MatchResult? Result, MatchResultDto? Pending, bool DecidedHere);

    private sealed record Participant(Guid UserId, int Slot);

    /// <summary>One participant's standing while the verdict is being worked out.</summary>
    private sealed class Standing
    {
        public required Guid UserId { get; init; }
        public required int Slot { get; init; }
        public bool Reported { get; set; }
        public bool Flagged { get; set; }
        public string? FlagReason { get; set; }
        public Dictionary<string, long> Values { get; } = new(StringComparer.Ordinal);
        public int? Placement { get; set; }
        public bool IsWinner { get; set; }
    }

    private async Task<Outcome> TryDecideAsync(
        MultiplayerSession session,
        DateTime now,
        bool allowDeadline,
        CancellationToken cancellationToken)
    {
        var mode = session.ModeId is { } modeId
            ? await _dbContext.GameModes.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modeId, cancellationToken)
            : null;

        var rule = mode?.WinRule;

        // A match that never started has nobody in it to place.
        if (session.StartedAtUtc is not { } startedAt)
            return new Outcome(null, await PendingAsync(session, rule, [], cancellationToken), false);

        var participants = await ParticipantsAsync(session.Id, startedAt, cancellationToken);
        var standings = await MeasureAsync(session, startedAt, now, participants, rule, cancellationToken);

        var allReported = standings.Count > 0 && standings.All(s => s.Reported);
        var graceOver = session.State.IsTerminal()
                        && session.EndedAtUtc is { } ended
                        && ended <= now.AddSeconds(-_options.MatchResultGraceSeconds);

        if (!allReported && !(allowDeadline && graceOver))
            return new Outcome(null, await PendingAsync(session, rule, standings, cancellationToken), false);

        if (rule is not null)
            Place(standings, rule);

        var result = new MatchResult
        {
            SessionId = session.Id,
            GameId = session.GameId,
            ModeId = session.ModeId,
            EventId = session.EventId,
            LessonId = session.LessonId,
            State = rule is null ? MatchResultState.Unranked : MatchResultState.Decided,
            WinRuleJson = rule?.ToJson(),
            ParticipantCount = standings.Count,
            ReportedCount = standings.Count(s => s.Reported),
            DecidedBy = allReported ? AllReported : Deadline,
            MatchStartedAtUtc = startedAt,
            DecidedAtUtc = now,
            Placements = standings.Select(s => new MatchPlacement
            {
                SessionId = session.Id,
                UserId = s.UserId,
                Slot = s.Slot,
                Placement = s.Placement,
                IsWinner = s.IsWinner,
                Forfeited = !s.Reported,
                Flagged = s.Flagged,
                FlagReason = s.FlagReason,
                ValuesJson = JsonSerializer.Serialize(s.Values, Json)
            }).ToList()
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.MatchResults.Add(result);

        // Each player hears the verdict is in, with the same commit that writes it: a result screen
        // waiting on its poll can stop waiting, and a player who left the app sees it on return.
        foreach (var standing in standings)
        {
            _events.Stage(standing.UserId, PlayerEventTypes.MatchResultReady, new
            {
                sessionId = session.Id,
                state = result.State,
                placement = standing.Placement,
                isWinner = standing.IsWinner,
                forfeited = !standing.Reported
            }, now.AddDays(1));
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another request decided it first — a read and the sweeper, or two reads. The key let
            // exactly one verdict in; this one reports that one.
            await transaction.RollbackAsync(cancellationToken);
            Detach();

            return new Outcome(await ReadAsync(session.Id, cancellationToken), null, false);
        }

        // Ratings move with the verdict, in its transaction — and only for a match the server formed
        // as rated. A forfeit arrives here placed last, so leaving a ranked match early is a loss.
        // First, so a tier reached in this match is in the same stream write as the match itself.
        IReadOnlyDictionary<Guid, int> tiersReached = new Dictionary<Guid, int>();

        if (session is { IsRated: true, ModeId: { } ratedMode } && result.State == MatchResultState.Decided && result.ReportedCount > 0)
            tiersReached = await _ratings.ApplyAsync(
                session.Id,
                ratedMode,
                standings.Where(s => s.Placement is not null)
                    .Select(s => new RatedPlacement(s.UserId, s.Placement!.Value, s.IsWinner, Forfeited: !s.Reported))
                    .ToList(),
                cancellationToken);

        // In the verdict's own transaction: a result whose MATCHES_WON never reached the stream would
        // be a win no board or quest ever saw, and a stream row without its result would be a win
        // nobody can explain.
        await EmitAsync(session, result, standings, tiersReached, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        MultiplayerMetrics.MatchResults.Add(1,
            MultiplayerMetrics.Tag("state", result.State.ToString()),
            MultiplayerMetrics.Tag("decided_by", result.DecidedBy));

        _logger.LogInformation(
            "Match {SessionId} decided ({State}, {DecidedBy}): {Reported}/{Participants} reported, winners {Winners}.",
            session.Id, result.State, result.DecidedBy, result.ReportedCount, result.ParticipantCount,
            string.Join(",", standings.Where(s => s.IsWinner).Select(s => s.Slot)));

        return new Outcome(result, null, true);
    }

    /// <summary>
    /// Everyone who held a seat when the match started. A seat taken before the start and not given
    /// up before it; the most recent such seat when someone left and rejoined in the lobby.
    /// </summary>
    private async Task<List<Participant>> ParticipantsAsync(
        Guid sessionId, DateTime startedAt, CancellationToken cancellationToken)
    {
        var seats = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId
                        && p.Status != SessionPlayerStatus.Removed
                        && p.JoinedAtUtc <= startedAt
                        && (p.LeftAtUtc == null || p.LeftAtUtc >= startedAt))
            .Select(p => new { p.UserId, p.Slot, p.JoinedAtUtc })
            .ToListAsync(cancellationToken);

        return seats
            .GroupBy(s => s.UserId)
            .Select(g => g.OrderByDescending(s => s.JoinedAtUtc).First())
            .OrderBy(s => s.Slot)
            .Select(s => new Participant(s.UserId, s.Slot))
            .ToList();
    }

    /// <summary>
    /// What each participant has reported, and the value of every metric the rule ranks on.
    /// <para>
    /// **Runs:** every settled run the player opened for this session. Counts and durations are
    /// summed across them; the outcome is the last one's. **Answers:** the player's *first* graded
    /// attempt after the start, on the match's lesson — one attempt per match, so resubmitting the
    /// same questions cannot buy a better score.
    /// </para>
    /// </summary>
    private async Task<List<Standing>> MeasureAsync(
        MultiplayerSession session,
        DateTime startedAt,
        DateTime now,
        List<Participant> participants,
        MatchWinRule? rule,
        CancellationToken cancellationToken)
    {
        var ids = participants.Select(p => p.UserId).ToList();

        var runs = await _dbContext.Runs
            .AsNoTracking()
            .Where(r => r.SessionId == session.Id && r.State == RunState.Settled && ids.Contains(r.UserId))
            .Select(r => new { r.UserId, r.DurationMs, r.Outcome, r.PickupsJson, r.EndedAtUtc })
            .ToListAsync(cancellationToken);

        var attempts = await _dbContext.MatchAttemptScores
            .AsNoTracking()
            .Where(a => a.SessionId == session.Id && ids.Contains(a.UserId) && a.SubmittedAtUtc >= startedAt)
            .OrderBy(a => a.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        // Recorded only on the match's own lesson; checked again here so the verdict does not depend
        // on the writer having got that right.
        var firstAttempt = attempts
            .Where(a => a.LessonId == session.LessonId)
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.First());

        var rates = rule is null
            ? new Dictionary<string, double?>()
            : await RatesAsync(session.GameId, rule, cancellationToken);

        // No run can have been played for longer than the match lasted.
        var matchWindowMs = (long)Math.Max(0, ((session.EndedAtUtc ?? now) - startedAt).TotalMilliseconds);

        var standings = new List<Standing>(participants.Count);

        foreach (var participant in participants)
        {
            var standing = new Standing { UserId = participant.UserId, Slot = participant.Slot };
            var theirRuns = runs.Where(r => r.UserId == participant.UserId).OrderBy(r => r.EndedAtUtc).ToList();
            var attempt = firstAttempt.GetValueOrDefault(participant.UserId);

            var hasRun = theirRuns.Count > 0;
            var hasAnswers = attempt is not null;

            standing.Reported = rule is null
                ? hasRun || hasAnswers
                : (!rule.UsesRun || hasRun) && (!rule.UsesAnswers || hasAnswers);

            if (rule is null || !standing.Reported)
            {
                standings.Add(standing);
                continue;
            }

            var durationMs = Math.Min(theirRuns.Sum(r => (long)r.DurationMs), matchWindowMs);
            var signals = SumSignals(theirRuns.Select(r => r.PickupsJson));
            var problems = new List<string>();

            foreach (var criterion in rule.Criteria)
            {
                standing.Values[criterion.Metric] = criterion.Metric switch
                {
                    MatchMetrics.Outcome => theirRuns[^1].Outcome == RunOutcome.Completed ? 1 : 0,
                    MatchMetrics.DurationMs => durationMs,
                    MatchMetrics.CorrectAnswers => attempt!.CorrectCount,
                    MatchMetrics.Accuracy => attempt!.TotalCount > 0
                        ? (long)Math.Round(attempt.CorrectCount * 100.0 / attempt.TotalCount, MidpointRounding.AwayFromZero)
                        : 0,
                    _ => BoundedSignal(criterion.Metric, signals, durationMs, rates, problems)
                };
            }

            if (problems.Count > 0)
            {
                standing.Flagged = true;
                standing.FlagReason = string.Join(",", problems);
            }

            standings.Add(standing);
        }

        return standings;
    }

    /// <summary>
    /// A reported count, bounded by what the time played makes possible — the same per-kind rate the
    /// economy's pricer uses, and deliberately **not** its per-run or daily caps: those are about what
    /// a child may be paid, and a child who has hit today's coin limit has not played any worse.
    /// Reporting past the rate is recorded as a problem, which places the player after every clean one.
    /// </summary>
    private long BoundedSignal(
        string metric,
        IReadOnlyDictionary<string, long> signals,
        long durationMs,
        IReadOnlyDictionary<string, double?> rates,
        List<string> problems)
    {
        var kind = MatchMetrics.SignalKindOf(metric)!;
        var reported = signals.GetValueOrDefault(kind);
        var perSecond = rates.GetValueOrDefault(kind) ?? _runOptions.MaxPickupsPerSecond;

        if (perSecond <= 0)
            return reported;

        var ceiling = (long)Math.Floor(perSecond * Math.Max(1.0, durationMs / 1000.0));

        if (reported <= ceiling)
            return reported;

        problems.Add($"rate_exceeded:{kind}");
        return ceiling;
    }

    /// <summary>Each signal kind's per-second bound: the game's own valuation row, else the platform's.</summary>
    private async Task<IReadOnlyDictionary<string, double?>> RatesAsync(
        Guid gameId, MatchWinRule rule, CancellationToken cancellationToken)
    {
        var kinds = rule.Criteria.Select(c => MatchMetrics.SignalKindOf(c.Metric)).OfType<string>().ToList();

        if (kinds.Count == 0)
            return new Dictionary<string, double?>();

        var rows = await _dbContext.SignalValuations
            .AsNoTracking()
            .Where(v => v.Enabled && kinds.Contains(v.SignalKind) && (v.GameId == gameId || v.GameId == null))
            .Select(v => new { v.SignalKind, v.GameId, v.MaxPerSecond })
            .ToListAsync(cancellationToken);

        var rates = new Dictionary<string, double?>(StringComparer.Ordinal);

        // The game's own row last, so it wins over the platform default for the same kind.
        foreach (var row in rows.OrderBy(r => r.GameId.HasValue))
            rates[row.SignalKind] = row.MaxPerSecond;

        return rates;
    }

    private static Dictionary<string, long> SumSignals(IEnumerable<string> pickupsJson)
    {
        var totals = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var json in pickupsJson)
        {
            List<StoredSignal>? rows;

            try
            {
                rows = JsonSerializer.Deserialize<List<StoredSignal>>(json, RunJson.Options);
            }
            catch (JsonException)
            {
                continue;
            }

            foreach (var row in rows ?? [])
            {
                if (SignalKinds.Normalise(row.Kind) is { } kind && row.Count > 0)
                    totals[kind] = totals.GetValueOrDefault(kind) + row.Count;
            }
        }

        return totals;
    }

    private sealed class StoredSignal
    {
        public string? Kind { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Places everyone: reported before forfeited, clean before flagged, then the rule's criteria in
    /// order. Ties share a placement (standard competition ranking: 1, 1, 3).
    /// </summary>
    private static void Place(List<Standing> standings, MatchWinRule rule)
    {
        int Compare(Standing a, Standing b)
        {
            if (a.Reported != b.Reported) return a.Reported ? -1 : 1;
            if (!a.Reported) return 0;
            if (a.Flagged != b.Flagged) return a.Flagged ? 1 : -1;

            foreach (var criterion in rule.Criteria)
            {
                var left = a.Values.GetValueOrDefault(criterion.Metric);
                var right = b.Values.GetValueOrDefault(criterion.Metric);

                if (left != right)
                    return criterion.Order == MatchRankOrder.LowerWins ? left.CompareTo(right) : right.CompareTo(left);
            }

            return 0;
        }

        var ordered = standings
            .OrderBy(s => s, Comparer<Standing>.Create(Compare))
            .ThenBy(s => s.Slot)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].Placement = index > 0 && Compare(ordered[index - 1], ordered[index]) == 0
                ? ordered[index - 1].Placement
                : index + 1;
        }

        // First place counts as a win only for a player who actually played and ranked clean: a table
        // where everyone forfeited, or where the best result was flagged, has no winner.
        foreach (var standing in ordered)
            standing.IsWinner = standing.Placement == 1 && standing.Reported && !standing.Flagged;
    }

    /// <summary>
    /// Raises the match into the game-results stream. <c>MATCHES_PLAYED</c> for everyone who
    /// reported; <c>MATCHES_WON</c> for the winners — **only when at least two players reported**,
    /// because a walkover is a result, not a win, and a second account that joins and forfeits must
    /// not be a way to farm wins.
    /// </summary>
    private async Task EmitAsync(
        MultiplayerSession session,
        MatchResult result,
        List<Standing> standings,
        IReadOnlyDictionary<Guid, int> tiersReached,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // A match is play for its own sake unless it is an event entry: it ranks and pays as its mode
        // allows, and it never moves mastery — the graded attempts inside it already did that.
        var context = session.EventId is null ? PlayContextKind.FreePlay : PlayContextKind.Event;
        var play = await _play.DescribeAsync(session.ModeId, context, session.EventId, cancellationToken);

        if (play.Policy == PlaySettlementPolicy.Nothing)
            return;

        var reported = standings.Where(s => s.Reported).ToList();
        var winsCount = reported.Count >= 2;

        var ids = reported.Select(s => s.UserId).ToList();
        var grades = await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => (Guid?)p.GradeId, cancellationToken);

        foreach (var standing in reported)
        {
            var metrics = new List<GameResultDraft> { new(LeaderboardMetrics.MatchesPlayed, 1) };

            if (standing.IsWinner && winsCount)
                metrics.Add(new GameResultDraft(LeaderboardMetrics.MatchesWon, 1));

            // A ranked tier reached this season, for "reach gold this month".
            if (tiersReached.TryGetValue(standing.UserId, out var tier))
                metrics.Add(new GameResultDraft(LeaderboardMetrics.RankedTier, tier));

            await _results.RecordAsync(
                new GameResultContext
                {
                    UserId = standing.UserId,
                    GameId = session.GameId,
                    SourceId = session.Id,
                    OccurredAtUtc = now,
                    GradeId = grades.GetValueOrDefault(standing.UserId),
                    LangId = session.LangId,

                    // One emission per player per match, enforced by the stream's own unique index.
                    RequestId = $"match:{session.Id:N}",
                    SourceType = GameResultSource.Session,
                    ModeId = session.ModeId,
                    Context = context,
                    EventId = session.EventId,
                    CountsForRanking = play.Policy.Ranks,
                    PreFlagged = standing.Flagged,
                    PreFlagReason = standing.FlagReason,
                    Metrics = metrics
                },
                cancellationToken);

            // Inline, like a run's settlement: "win three matches" should tick over on the results
            // screen, not on the next batch pass.
            await _objectives.ProjectForUserAsync(standing.UserId, cancellationToken);
        }
    }

    // ---- mapping -------------------------------------------------------------------------------

    private Task<MatchResult?> ReadAsync(Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.MatchResults
            .AsNoTracking()
            .Include(r => r.Placements)
            .FirstOrDefaultAsync(r => r.SessionId == sessionId, cancellationToken);

    private async Task<MatchResultDto> MapAsync(MatchResult result, CancellationToken cancellationToken)
    {
        var names = await _names.ResolveAsync(result.Placements.Select(p => p.UserId).ToList(), cancellationToken);

        return new MatchResultDto
        {
            SessionId = result.SessionId,
            State = result.State switch
            {
                MatchResultState.Decided => MatchResultStatus.Decided,
                MatchResultState.Unranked => MatchResultStatus.Unranked,
                MatchResultState.Void => MatchResultStatus.Void,
                _ => MatchResultStatus.Unknown
            },
            DecidedBy = result.DecidedBy,
            DecidedAtUtc = DateTime.SpecifyKind(result.DecidedAtUtc, DateTimeKind.Utc),
            Rule = RuleDto(MatchWinRule.Parse(result.WinRuleJson)),
            Placements = result.Placements
                .OrderBy(p => p.Placement ?? int.MaxValue)
                .ThenBy(p => p.Slot)
                .Select(p => new MatchPlacementDto
                {
                    Slot = p.Slot,
                    UserId = p.UserId,
                    DisplayName = names.GetValueOrDefault(p.UserId),
                    Placement = p.Placement,
                    IsWinner = p.IsWinner,
                    Reported = !p.Forfeited,
                    Forfeited = p.Forfeited,
                    Flagged = p.Flagged,
                    Values = ParseValues(p.ValuesJson)
                })
                .ToList(),
            ServerTimeUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Where an undecided match stands: who is in it, and whose result has arrived — what a
    /// "waiting for 1 player" screen needs.
    /// </summary>
    private async Task<MatchResultDto> PendingAsync(
        MultiplayerSession session, MatchWinRule? rule, List<Standing> standings, CancellationToken cancellationToken)
    {
        var names = await _names.ResolveAsync(standings.Select(s => s.UserId).ToList(), cancellationToken);

        return new MatchResultDto
        {
            SessionId = session.Id,
            State = MatchResultStatus.Pending,
            Rule = RuleDto(rule),
            Placements = standings
                .OrderBy(s => s.Slot)
                .Select(s => new MatchPlacementDto
                {
                    Slot = s.Slot,
                    UserId = s.UserId,
                    DisplayName = names.GetValueOrDefault(s.UserId),
                    Reported = s.Reported
                })
                .ToList(),
            ServerTimeUtc = DateTime.UtcNow
        };
    }

    private static List<MatchWinCriterionDto> RuleDto(MatchWinRule? rule) => MatchRuleMapping.ToDto(rule);

    private static string TrustToken(MatchMetricTrust trust) => MatchRuleMapping.TrustToken(trust);

    private static Dictionary<string, long> ParseValues(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, long>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private bool AllowDeadline(DateTime now) =>
        !_warmup.IsWarmingUp(now, TimeSpan.FromSeconds(Math.Max(_options.SessionTimeoutSeconds, _options.MatchResultGraceSeconds)));

    private void Detach()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }

    private static ServiceResult<MatchResultDto> NotFound(Guid sessionId) =>
        ServiceResult<MatchResultDto>.Failure(
            ApiErrors.SessionNotFound,
            ServiceErrorKind.NotFound,
            $"Session {sessionId} does not exist, or the caller is not a member of it.");
}
