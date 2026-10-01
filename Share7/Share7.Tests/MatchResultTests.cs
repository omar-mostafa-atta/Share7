using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Progress.Models;
using Share7.Application.Runs.Models;
using Share7.Domain.Constants;
using Share7.Domain.Leaderboards;
using Share7.Domain.Multiplayer;
using Share7.Domain.Play;
using Share7.Domain.Runs;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Matches decided by the server, under each mode's own win rule.
/// <para>
/// **Every placement here is derived from what the players' own settled runs and graded attempts
/// show.** No client says who won. The tests drive real runs through the real run service and real
/// attempts through the real grading path, because a verdict computed from rows a test made up would
/// prove nothing about the rows production actually writes.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MatchResultTests
{
    private readonly SqlServerFixture _fixture;

    public MatchResultTests(SqlServerFixture fixture) => _fixture = fixture;

    private sealed record Match(CurriculumPathFixture Path, GameMode Mode, Guid SessionId, IReadOnlyList<Guid> Players);

    /// <summary>
    /// A versus mode with <paramref name="rule"/>, and a match of it that has started with
    /// <paramref name="players"/> seated (the host first). Backdated five minutes, so the match has
    /// been running long enough for real play times to fit inside it.
    /// </summary>
    private async Task<Match> StartMatchAsync(int players, params (string Metric, string Order)[] rule)
    {
        await using var context = _fixture.CreateContext();

        var path = await TestData.CreateCurriculumPathAsync(context);
        await MultiplayerTest.SetSeatsAsync(context, path.GameId, 1, 4);

        var mode = await context.AddModeAsync(path.GameId, isDefault: true, topologies: PlayTopologies.Solo | PlayTopologies.Versus);
        var ruleJson = rule.Length == 0 ? null : MatchWinRule.Build(rule.Select(r => ((string?)r.Metric, (string?)r.Order))).Rule!.ToJson();

        await context.GameModes
            .Where(m => m.Id == mode.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.WinRuleJson, ruleJson).SetProperty(m => m.MaxPlayers, 4));

        var hostId = await TestData.CreateUserAsync(context);
        var sessions = MultiplayerTest.Sessions(context);

        var created = await sessions.CreateAsync(hostId, new CreateMultiplayerSessionRequest
        {
            GameId = path.GameId,
            ModeKey = mode.ModeKey,
            TransportSessionName = MultiplayerTest.NewTransportName(),
            MaxPlayers = 4,
            ProtocolVersion = 1,
            CurriculumPath = new CurriculumPathDto { LessonId = path.LessonId }
        });
        Assert.True(created.Succeeded, created.Error?.Code);

        var sessionId = created.Value!.Id;
        await sessions.StartAsync(hostId, sessionId, new StartMultiplayerSessionRequest());

        var seated = new List<Guid> { hostId };
        for (var i = 1; i < players; i++)
            seated.Add(await MultiplayerTest.JoinAsync(_fixture, sessionId));

        await using var starting = _fixture.CreateContext();
        var started = await MultiplayerTest.Sessions(starting).StartAsync(hostId, sessionId, new StartMultiplayerSessionRequest());
        Assert.Equal(MultiplayerSessionState.Running, started.Value!.State);

        await BackdateAsync(starting, sessionId, seconds: 300);

        return new Match(path, mode, sessionId, seated);
    }

    private static async Task BackdateAsync(ApplicationDbContext context, Guid sessionId, int seconds)
    {
        await context.MultiplayerSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(set => set
            .SetProperty(s => s.CreatedAtUtc, s => s.CreatedAtUtc.AddSeconds(-seconds))
            .SetProperty(s => s.StartedAtUtc, s => s.StartedAtUtc!.Value.AddSeconds(-seconds)));

        await context.MultiplayerSessionPlayers.Where(p => p.SessionId == sessionId).ExecuteUpdateAsync(set => set
            .SetProperty(p => p.JoinedAtUtc, p => p.JoinedAtUtc.AddSeconds(-seconds)));
    }

    /// <summary>One player's run in the match: started with the session id, played, settled.</summary>
    private async Task SettleRunAsync(
        Match match, Guid userId, int durationMs, RunOutcome outcome, params (string Kind, int Count)[] signals)
    {
        await using var context = _fixture.CreateContext();
        var runs = RunTestExtensions.CreateRunService(context);

        var started = await runs.StartAsync(userId, new StartRunRequest
        {
            GameId = match.Path.GameId,
            SessionId = match.SessionId,
            ModeKey = match.Mode.ModeKey
        });
        Assert.True(started.Succeeded, started.Error?.Code);

        await context.AgeRunAsync(started.Value!.RunId, seconds: 240);

        var settled = await runs.SettleAsync(userId, started.Value.RunId, new SubmitRunResultRequest
        {
            Signals = [.. signals.Select(s => new RunSignalReport { Kind = s.Kind, Count = s.Count })],
            DurationMs = durationMs,
            Outcome = outcome.ToString()
        });
        Assert.True(settled.Succeeded, settled.Error?.Code);
    }

    private async Task<MatchResultDto> ResultAsync(Match match, Guid asUser)
    {
        await using var context = _fixture.CreateContext();
        var result = await MultiplayerTest.Results(context).GetAsync(asUser, match.SessionId);
        Assert.True(result.Succeeded, result.Error?.Code);
        return result.Value!;
    }

    private static MatchPlacementDto Of(MatchResultDto result, Guid userId) =>
        result.Placements.Single(p => p.UserId == userId);

    // -----------------------------------------------------------------------------------------
    // rules an operator chooses per mode
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Last_one_standing_then_longest_alive_then_most_kills()
    {
        // The battle-royale rule, authored as data: survived, then time alive, then kills.
        var match = await StartMatchAsync(3,
            (MatchMetrics.Outcome, "higher"), (MatchMetrics.DurationMs, "higher"), ("signal:kill", "higher"));

        var (a, b, c) = (match.Players[0], match.Players[1], match.Players[2]);

        await SettleRunAsync(match, a, 60_000, RunOutcome.Completed, ("kill", 2));
        await SettleRunAsync(match, b, 90_000, RunOutcome.Failed, ("kill", 9));   // most kills, but died
        await SettleRunAsync(match, c, 60_000, RunOutcome.Completed, ("kill", 5));

        var result = await ResultAsync(match, a);

        Assert.Equal(MatchResultStatus.Decided, result.State);
        Assert.Equal("all_reported", result.DecidedBy);

        // Survivors above the fallen; between the two survivors, equal time alive, so kills decide.
        Assert.Equal(1, Of(result, c).Placement);
        Assert.Equal(2, Of(result, a).Placement);
        Assert.Equal(3, Of(result, b).Placement);
        Assert.True(Of(result, c).IsWinner);
        Assert.Equal(5, Of(result, c).Values["signal:kill"]);
    }

    [Fact]
    public async Task Most_correct_answers_then_fastest_from_graded_attempts()
    {
        var match = await StartMatchAsync(2, (MatchMetrics.CorrectAnswers, "higher"), (MatchMetrics.DurationMs, "lower"));
        var (host, guest) = (match.Players[0], match.Players[1]);

        await using (var setup = _fixture.CreateContext())
        {
            // Four questions in all: the fixture's one plus three more.
            await setup.AddQuestionsAsync(match.Path.LessonId, 3);
            await setup.UnlockLessonAsync(host, match.Path);
            await setup.UnlockLessonAsync(guest, match.Path);
        }

        // Answers are graded by the server from the attempt; the run supplies the time.
        await SubmitAttemptAsync(match, host, correct: 3);
        await SubmitAttemptAsync(match, guest, correct: 3);
        await SettleRunAsync(match, host, 50_000, RunOutcome.Completed);
        await SettleRunAsync(match, guest, 40_000, RunOutcome.Completed);

        var result = await ResultAsync(match, host);

        // Level on answers, so the faster finish wins.
        Assert.Equal(3, Of(result, host).Values[MatchMetrics.CorrectAnswers]);
        Assert.Equal(3, Of(result, guest).Values[MatchMetrics.CorrectAnswers]);
        Assert.Equal(1, Of(result, guest).Placement);
        Assert.Equal(2, Of(result, host).Placement);
        Assert.Contains(result.Rule, c => c.Metric == MatchMetrics.CorrectAnswers && c.Trust == "verified");
    }

    [Fact]
    public async Task Equal_players_share_a_placement()
    {
        var match = await StartMatchAsync(3, ("signal:coin", "higher"));
        var (a, b, c) = (match.Players[0], match.Players[1], match.Players[2]);

        await SettleRunAsync(match, a, 60_000, RunOutcome.Completed, ("coin", 7));
        await SettleRunAsync(match, b, 60_000, RunOutcome.Completed, ("coin", 7));
        await SettleRunAsync(match, c, 60_000, RunOutcome.Completed, ("coin", 3));

        var result = await ResultAsync(match, a);

        // Standard competition ranking: 1, 1, 3. Telling one of two equal children they came second
        // would be a coin toss, not a result.
        Assert.Equal(1, Of(result, a).Placement);
        Assert.Equal(1, Of(result, b).Placement);
        Assert.Equal(3, Of(result, c).Placement);
        Assert.True(Of(result, a).IsWinner);
        Assert.True(Of(result, b).IsWinner);
    }

    [Fact]
    public async Task A_count_the_time_played_cannot_explain_places_below_every_clean_player()
    {
        var match = await StartMatchAsync(2, ("signal:kill", "higher"));
        var (cheat, honest) = (match.Players[0], match.Players[1]);

        // Ten thousand kills in a minute is past the per-second bound of any plausible game.
        await SettleRunAsync(match, cheat, 60_000, RunOutcome.Completed, ("kill", 10_000));
        await SettleRunAsync(match, honest, 60_000, RunOutcome.Completed, ("kill", 3));

        var result = await ResultAsync(match, honest);

        Assert.Equal(1, Of(result, honest).Placement);
        Assert.True(Of(result, honest).IsWinner);

        var flagged = Of(result, cheat);
        Assert.True(flagged.Flagged);
        Assert.False(flagged.IsWinner);
        Assert.Equal(2, flagged.Placement);

        // Clamped to what was possible, not the claim, so even the stored figure is not the lie.
        Assert.True(flagged.Values["signal:kill"] < 10_000);
    }

    // -----------------------------------------------------------------------------------------
    // when a match is decided
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_match_is_pending_until_everyone_has_reported()
    {
        var match = await StartMatchAsync(2, ("signal:coin", "higher"));
        await SettleRunAsync(match, match.Players[0], 60_000, RunOutcome.Completed, ("coin", 4));

        var result = await ResultAsync(match, match.Players[0]);

        // What a "waiting for 1 player" screen needs: who is in, whose result is in.
        Assert.Equal(MatchResultStatus.Pending, result.State);
        Assert.True(Of(result, match.Players[0]).Reported);
        Assert.False(Of(result, match.Players[1]).Reported);
        Assert.All(result.Placements, p => Assert.Null(p.Placement));

        await using var check = _fixture.CreateContext();
        Assert.False(await check.MatchResults.AnyAsync(r => r.SessionId == match.SessionId));
    }

    [Fact]
    public async Task A_player_who_never_reports_forfeits_once_the_grace_period_is_over()
    {
        var match = await StartMatchAsync(2, ("signal:coin", "higher"));
        var (present, missing) = (match.Players[0], match.Players[1]);

        await SettleRunAsync(match, present, 60_000, RunOutcome.Completed, ("coin", 4));

        await using (var ending = _fixture.CreateContext())
        {
            await MultiplayerTest.Sessions(ending).CloseAsync(present, match.SessionId, new CloseMultiplayerSessionRequest());
            await ending.MultiplayerSessions.Where(s => s.Id == match.SessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.EndedAtUtc, DateTime.UtcNow.AddMinutes(-5)));
        }

        var result = await ResultAsync(match, present);

        Assert.Equal(MatchResultStatus.Decided, result.State);
        Assert.Equal("deadline", result.DecidedBy);
        Assert.True(Of(result, present).IsWinner);
        Assert.True(Of(result, missing).Forfeited);
        Assert.Equal(2, Of(result, missing).Placement);

        // **A walkover is a result, not a win.** One player reporting is not a match anybody won —
        // otherwise a second account that joins and quits is a way to farm wins.
        await using var check = _fixture.CreateContext();
        Assert.False(await check.GameResults.AnyAsync(r =>
            r.SourceId == match.SessionId && r.Metric == LeaderboardMetrics.MatchesWon));
        Assert.True(await check.GameResults.AnyAsync(r =>
            r.SourceId == match.SessionId && r.Metric == LeaderboardMetrics.MatchesPlayed && r.UserId == present));
        Assert.False(await check.GameResults.AnyAsync(r =>
            r.SourceId == match.SessionId && r.UserId == missing));
    }

    [Fact]
    public async Task A_mode_with_no_rule_records_who_played_and_crowns_nobody()
    {
        var match = await StartMatchAsync(2);

        await SettleRunAsync(match, match.Players[0], 60_000, RunOutcome.Completed);
        await SettleRunAsync(match, match.Players[1], 60_000, RunOutcome.Completed);

        var result = await ResultAsync(match, match.Players[0]);

        Assert.Equal(MatchResultStatus.Unranked, result.State);
        Assert.Empty(result.Rule);
        Assert.All(result.Placements, p => { Assert.Null(p.Placement); Assert.False(p.IsWinner); });

        await using var check = _fixture.CreateContext();
        Assert.Equal(2, await check.GameResults.CountAsync(r =>
            r.SourceId == match.SessionId && r.Metric == LeaderboardMetrics.MatchesPlayed));
    }

    [Fact]
    public async Task A_decided_match_reaches_the_results_stream_once()
    {
        var match = await StartMatchAsync(2, ("signal:coin", "higher"));
        var (winner, other) = (match.Players[0], match.Players[1]);

        await SettleRunAsync(match, winner, 60_000, RunOutcome.Completed, ("coin", 9));
        await SettleRunAsync(match, other, 60_000, RunOutcome.Completed, ("coin", 2));

        await ResultAsync(match, winner);
        await ResultAsync(match, other);
        await ResultAsync(match, winner);

        await using var check = _fixture.CreateContext();
        var rows = await check.GameResults.Where(r => r.SourceId == match.SessionId).ToListAsync();

        // What boards and quests consume: two played, one won — and a win for the winner only.
        Assert.Equal(2, rows.Count(r => r.Metric == LeaderboardMetrics.MatchesPlayed));
        var won = Assert.Single(rows, r => r.Metric == LeaderboardMetrics.MatchesWon);
        Assert.Equal(winner, won.UserId);
    }

    [Fact]
    public async Task Two_readers_deciding_at_once_write_one_verdict()
    {
        var match = await StartMatchAsync(2, ("signal:coin", "higher"));
        await SettleRunAsync(match, match.Players[0], 60_000, RunOutcome.Completed, ("coin", 9));
        await SettleRunAsync(match, match.Players[1], 60_000, RunOutcome.Completed, ("coin", 2));

        MatchResultDto? first = null;

        var race = new InterleavingInterceptor(async () =>
        {
            await using var other = _fixture.CreateContext();
            first = (await MultiplayerTest.Results(other).GetAsync(match.Players[1], match.SessionId)).Value;
        });

        await using var context = _fixture.CreateInterleavedContext(race);
        var second = await MultiplayerTest.Results(context).GetAsync(match.Players[0], match.SessionId);

        Assert.True(race.Fired);
        Assert.True(second.Succeeded, second.Error?.Code);
        Assert.Equal(first!.DecidedAtUtc, second.Value!.DecidedAtUtc);

        await using var check = _fixture.CreateContext();
        Assert.Single(await check.GameResults.Where(r => r.SourceId == match.SessionId && r.Metric == LeaderboardMetrics.MatchesWon).ToListAsync());
    }

    [Fact]
    public async Task Changing_a_modes_rule_does_not_re_decide_matches_already_played()
    {
        var match = await StartMatchAsync(2, ("signal:coin", "higher"));
        var (a, b) = (match.Players[0], match.Players[1]);

        await SettleRunAsync(match, a, 60_000, RunOutcome.Completed, ("coin", 9));
        await SettleRunAsync(match, b, 60_000, RunOutcome.Completed, ("coin", 2));

        Assert.True(Of(await ResultAsync(match, a), a).IsWinner);

        // The operator flips the rule afterwards: fewest coins now wins.
        await using (var edit = _fixture.CreateContext())
        {
            var flipped = MatchWinRule.Build([("signal:coin", "lower")]).Rule!.ToJson();
            await edit.GameModes.Where(m => m.Id == match.Mode.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.WinRuleJson, flipped));
        }

        var again = await ResultAsync(match, a);

        Assert.True(Of(again, a).IsWinner);
        Assert.Equal("higher", Assert.Single(again.Rule).Order);
    }

    [Fact]
    public async Task The_sweeper_decides_a_match_nobody_asked_about()
    {
        var match = await StartMatchAsync(2, ("signal:coin", "higher"));
        await SettleRunAsync(match, match.Players[0], 60_000, RunOutcome.Completed, ("coin", 9));
        await SettleRunAsync(match, match.Players[1], 60_000, RunOutcome.Completed, ("coin", 2));

        await using var context = _fixture.CreateContext();
        var swept = await MultiplayerTest.Sweeper(context).SweepAsync();

        Assert.True(swept.MatchesDecided >= 1);
        Assert.True(await context.MatchResults.AnyAsync(r => r.SessionId == match.SessionId));
    }

    [Fact]
    public async Task A_stranger_cannot_read_a_match_result()
    {
        var match = await StartMatchAsync(2, ("signal:coin", "higher"));

        await using var context = _fixture.CreateContext();
        var strangerId = await TestData.CreateUserAsync(context);

        var result = await MultiplayerTest.Results(context).GetAsync(strangerId, match.SessionId);

        Assert.Equal("SESSION_NOT_FOUND", result.Error?.Code);
    }

    // -----------------------------------------------------------------------------------------
    // answers reach a match only through a seat, and only on the match's lesson
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_attempt_naming_a_session_counts_only_for_a_seat_holder_on_the_matchs_lesson()
    {
        var match = await StartMatchAsync(2, (MatchMetrics.CorrectAnswers, "higher"));
        var player = match.Players[0];

        await using var setup = _fixture.CreateContext();
        var stranger = await TestData.CreateUserAsync(setup);
        await setup.UnlockLessonAsync(player, match.Path);
        await setup.UnlockLessonAsync(stranger, match.Path);

        await SubmitAttemptAsync(match, player, correct: 1);
        await SubmitAttemptAsync(match, stranger, correct: 1);

        await using var check = _fixture.CreateContext();
        var scores = await check.MatchAttemptScores.Where(s => s.SessionId == match.SessionId).ToListAsync();

        // The seat holder's graded score is on the match; naming a session you were never in does nothing.
        var score = Assert.Single(scores);
        Assert.Equal(player, score.UserId);
        Assert.Equal(1, score.CorrectCount);
    }

    private async Task SubmitAttemptAsync(Match match, Guid userId, int correct)
    {
        await using var context = _fixture.CreateContext();

        var questions = await context.Questions
            .Where(q => q.LessonId == match.Path.LessonId && q.LangId == LanguageIds.English)
            .OrderBy(q => q.RowNumber)
            .Select(q => new { q.Id, q.CorrectChoiceId, Wrong = q.Choices.Where(c => c.Id != q.CorrectChoiceId).Select(c => c.Id).First() })
            .ToListAsync();

        var request = new SubmitAttemptRequest
        {
            GameId = match.Path.GameId,
            LessonId = match.Path.LessonId,
            ModeKey = match.Mode.ModeKey,
            SessionId = match.SessionId,
            Answers = [.. questions.Select((q, index) => new SubmittedAnswer
            {
                QuestionId = q.Id,
                ChoiceId = index < correct ? q.CorrectChoiceId : q.Wrong
            })]
        };

        var result = await RewardTestExtensions.CreateProgressService(context, userId).SubmitAttemptAsync(userId, request);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }
    // -----------------------------------------------------------------------------------------
    // ranked: ratings move with the verdict, only for a match the server formed as rated
    // -----------------------------------------------------------------------------------------

    private async Task MarkRatedAsync(Match match)
    {
        await using var context = _fixture.CreateContext();
        await context.MultiplayerSessions.Where(s => s.Id == match.SessionId)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsRated, true).SetProperty(s => s.IsRanked, true));
    }

    [Fact]
    public async Task A_rated_match_moves_ratings_with_its_verdict_and_tells_each_player_their_rank()
    {
        var match = await StartMatchAsync(2, ("outcome", "higher"));
        var (winner, loser) = (match.Players[0], match.Players[1]);
        await MarkRatedAsync(match);

        await SettleRunAsync(match, winner, 60_000, RunOutcome.Completed);
        await SettleRunAsync(match, loser, 60_000, RunOutcome.Failed);

        var result = await ResultAsync(match, winner);
        Assert.Equal(MatchResultStatus.Decided, result.State);

        await using var check = _fixture.CreateContext();
        var ratings = await check.PlayerRatings.Where(r => r.ModeId == match.Mode.Id).ToDictionaryAsync(r => r.UserId);

        Assert.True(ratings[winner].Mu > RatingModel.InitialMu);
        Assert.True(ratings[loser].Mu < RatingModel.InitialMu);

        // The caller's own rank, and only theirs, rides on the result.
        Assert.NotNull(result.Ranked);
        Assert.True(result.Ranked!.IsPlacement);
        Assert.Null((await ResultAsync(match, loser)).Ranked!.NotCountedReason);
    }

    [Fact]
    public async Task Leaving_a_rated_match_is_a_loss_but_the_player_who_stayed_is_not_credited()
    {
        var match = await StartMatchAsync(2, ("outcome", "higher"));
        var (stayed, left) = (match.Players[0], match.Players[1]);
        await MarkRatedAsync(match);

        await SettleRunAsync(match, stayed, 60_000, RunOutcome.Completed);

        await using (var ending = _fixture.CreateContext())
        {
            await MultiplayerTest.Sessions(ending).CloseAsync(stayed, match.SessionId, new CloseMultiplayerSessionRequest());
            await ending.MultiplayerSessions.Where(s => s.Id == match.SessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.EndedAtUtc, DateTime.UtcNow.AddMinutes(-5)));
        }

        var result = await ResultAsync(match, stayed);
        Assert.Equal("deadline", result.DecidedBy);

        await using var check = _fixture.CreateContext();
        var ratings = await check.PlayerRatings.Where(r => r.ModeId == match.Mode.Id).ToDictionaryAsync(r => r.UserId);

        Assert.True(ratings[left].Mu < RatingModel.InitialMu);
        Assert.Equal(RatingModel.InitialMu, ratings[stayed].Mu, 6);
        Assert.Equal("walkover", result.Ranked!.NotCountedReason);
    }

    [Fact]
    public async Task A_room_the_client_called_ranked_is_never_rated()
    {
        var match = await StartMatchAsync(2, ("outcome", "higher"));

        await using (var setup = _fixture.CreateContext())
        {
            // The client's own flag: set, and meaningless for ratings.
            await setup.MultiplayerSessions.Where(s => s.Id == match.SessionId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsRanked, true));
        }

        await SettleRunAsync(match, match.Players[0], 60_000, RunOutcome.Completed);
        await SettleRunAsync(match, match.Players[1], 60_000, RunOutcome.Failed);

        var result = await ResultAsync(match, match.Players[0]);
        Assert.Equal(MatchResultStatus.Decided, result.State);
        Assert.Null(result.Ranked);

        await using var check = _fixture.CreateContext();
        Assert.False(await check.PlayerRatingChanges.AnyAsync(c => c.SessionId == match.SessionId));
    }
}
