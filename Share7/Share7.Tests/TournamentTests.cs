using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Application.Runs.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Audit;
using Share7.Domain.Multiplayer;
using Share7.Domain.Social;
using Share7.Domain.Runs;
using Share7.Domain.Play;
using Share7.Application.Play.Models;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class TournamentTests(SqlServerFixture fixture)
{
    private sealed record Cup(Guid Id, Guid Organiser, Guid Game, Guid Mode, Guid[] Players);

    [Fact]
    public async Task A_round_robin_league_advances_on_existing_pairing_results_to_complete()
    {
        var cup = await CreateAsync(3, TournamentFormat.RoundRobin); await EnterAsync(cup); await StartAsync(cup);
        for (var round = 1; round <= 3; round++)
        {
            await using var db = fixture.CreateContext(); var service = MultiplayerTest.Tournaments(db);
            var view = await service.GetAsync(cup.Organiser, cup.Id, true); Assert.Equal(round, view.Value!.CurrentRound);
            foreach (var match in view.Value.Rounds.Single(r => r.Round == round).Matches.Where(m => m.State != TournamentMatchState.Completed))
            {
                var decided = await service.DecideMatchAsync(cup.Organiser, cup.Id, match.Id,
                    new() { WinnerUserId = match.PlayerAUserId, Reason = "Operator rehearsal" }, true);
                Assert.True(decided.Succeeded, string.Join("; ", decided.Errors));
            }
        }
        await using var check = fixture.CreateContext();
        var final = await MultiplayerTest.Tournaments(check).GetAsync(cup.Organiser, cup.Id, true);
        Assert.Equal(TournamentState.Completed, final.Value!.State);
        Assert.All(final.Value.Entrants, e => Assert.NotNull(e.Placement));
    }

    private async Task<Cup> CreateAsync(int count = 4, TournamentFormat format = TournamentFormat.SingleElimination, int? cap = null, bool classroom = false)
    {
        await using var db = fixture.CreateContext();
        var path = await TestData.CreateCurriculumPathAsync(db);
        var mode = await db.AddModeAsync(path.GameId, isDefault: true);
        mode.WinRuleJson = MatchWinRule.Build([((string?)MatchMetrics.CorrectAnswers, (string?)"higher")]).Rule!.ToJson();
        var organiser = await TestData.CreateUserAsync(db);
        var players = new List<Guid>();
        for (var i = 0; i < count; i++) players.Add(await TestData.CreateUserAsync(db));
        var cohort = classroom ? await SocialTest.ClassAsync(db, players.ToArray(), organiser) : (Guid?)null;
        await db.SaveChangesAsync();
        var created = await MultiplayerTest.Tournaments(db).CreateAsync(organiser, new()
        {
            Title = "Fractions cup", GameId = path.GameId, ModeId = mode.Id, Format = format,
            MaxEntrants = cap ?? Math.Max(2, count), MatchMinutes = 10, CohortId = cohort
        }, asAdmin: !classroom);
        Assert.True(created.Succeeded, created.Error?.Code);
        return new(created.Value!.Id, organiser, path.GameId, mode.Id, players.ToArray());
    }

    private async Task EnterAsync(Cup cup)
    {
        foreach (var player in cup.Players)
        {
            await using var db = fixture.CreateContext();
            var entered = await MultiplayerTest.Tournaments(db).RegisterAsync(player, cup.Id);
            Assert.True(entered.Succeeded, entered.Error?.Code);
        }
    }

    private async Task<TournamentDto> ReadAsync(Cup cup)
    {
        await using var db = fixture.CreateContext();
        var result = await MultiplayerTest.Tournaments(db).GetAsync(cup.Organiser, cup.Id, asAdmin: true);
        Assert.True(result.Succeeded, result.Error?.Code);
        return result.Value!;
    }

    private async Task<TournamentDto> StartAsync(Cup cup)
    {
        await using var db = fixture.CreateContext();
        var result = await MultiplayerTest.Tournaments(db).StartAsync(cup.Organiser, cup.Id, asAdmin: true);
        Assert.True(result.Succeeded, result.Error?.Code);
        return result.Value!;
    }

    [Fact]
    public async Task Knockout_advances_and_emits_results_and_completion_once()
    {
        var cup = await CreateAsync(); await EnterAsync(cup); var view = await StartAsync(cup);
        for (var round = 1; round <= 2; round++)
        {
            foreach (var match in view.Rounds.Single(r => r.Round == round).Matches.Where(m => m.State != TournamentMatchState.Completed))
            {
                await using var db = fixture.CreateContext();
                var decided = await MultiplayerTest.Tournaments(db).DecideMatchAsync(cup.Organiser, cup.Id, match.Id,
                    new() { WinnerUserId = match.PlayerAUserId, Reason = "Verified match report" }, asAdmin: true);
                Assert.True(decided.Succeeded, decided.Error?.Code);
            }
            view = await ReadAsync(cup);
        }
        Assert.Equal(TournamentState.Completed, view.State);
        Assert.Equal(new int?[] { 1, 2, 3, 3 }, view.Entrants.Select(e => e.Placement).Order());
        await ReadAsync(cup); await ReadAsync(cup);
        await using var check = fixture.CreateContext();
        Assert.Equal(4, await check.GameResults.CountAsync(r => r.SourceId == cup.Id && r.Metric == LeaderboardMetrics.TournamentsPlayed));
        Assert.Equal(1, await check.GameResults.CountAsync(r => r.SourceId == cup.Id && r.Metric == LeaderboardMetrics.TournamentsWon));
        Assert.Equal(4, await check.PlayerEvents.CountAsync(e => e.Type == "multiplayer.tournament.completed" && cup.Players.Contains(e.RecipientUserId)));
    }

    [Fact]
    public async Task Duplicate_entry_takes_one_place_even_with_simultaneous_requests()
    {
        var cup = await CreateAsync(2, cap: 4);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = fixture.CreateContext();
            return await MultiplayerTest.Tournaments(db).RegisterAsync(cup.Players[0], cup.Id);
        }));
        Assert.All(results, r => Assert.True(r.Succeeded, r.Error?.Code));
        Assert.Equal(1, (await ReadAsync(cup)).EntrantCount);
    }

    [Fact]
    public async Task Concurrent_last_place_never_exceeds_the_tournament_cap()
    {
        var cup = await CreateAsync(20, cap: 2);
        var results = await Task.WhenAll(cup.Players.Select(async player =>
        {
            await using var db = fixture.CreateContext();
            return await MultiplayerTest.Tournaments(db).RegisterAsync(player, cup.Id);
        }));
        Assert.Equal(2, results.Count(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal("TOURNAMENT_FULL", r.Error?.Code));
        Assert.Equal(2, (await ReadAsync(cup)).EntrantCount);
    }

    [Fact]
    public async Task A_duplicate_entry_still_succeeds_when_the_tournament_is_full()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup);
        await using var db = fixture.CreateContext();
        Assert.True((await MultiplayerTest.Tournaments(db).RegisterAsync(cup.Players[0], cup.Id)).Succeeded);
        Assert.Equal(2, (await ReadAsync(cup)).EntrantCount);
    }

    [Fact]
    public async Task Withdraw_before_start_frees_a_place_and_reentry_is_possible()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup);
        await using (var db = fixture.CreateContext())
            Assert.True((await MultiplayerTest.Tournaments(db).WithdrawAsync(cup.Players[0], cup.Id)).Succeeded);
        Assert.Equal(1, (await ReadAsync(cup)).EntrantCount);
        await using (var db = fixture.CreateContext())
            Assert.True((await MultiplayerTest.Tournaments(db).RegisterAsync(cup.Players[0], cup.Id)).Succeeded);
        Assert.Equal(2, (await ReadAsync(cup)).EntrantCount);
    }

    [Fact]
    public async Task Too_few_players_cancels_instead_of_declaring_a_champion()
    {
        var cup = await CreateAsync(1); await EnterAsync(cup);
        var view = await StartAsync(cup);
        Assert.Equal(TournamentState.Cancelled, view.State);
        Assert.Equal("too_few_players", view.CancelReason);
        Assert.Empty(view.Rounds);
    }

    [Fact]
    public async Task Two_simultaneous_starts_write_one_bracket()
    {
        var cup = await CreateAsync(); await EnterAsync(cup);
        await Task.WhenAll(StartAsync(cup), StartAsync(cup));
        var view = await ReadAsync(cup);
        Assert.Equal(2, Assert.Single(view.Rounds).Matches.Count);
        await using var db = fixture.CreateContext();
        Assert.Equal(4, await db.PlayerEvents.CountAsync(e => e.Type == "multiplayer.tournament.match_ready" && cup.Players.Contains(e.RecipientUserId)));
    }

    [Fact]
    public async Task Classroom_membership_controls_visibility_and_organising()
    {
        var cup = await CreateAsync(2, classroom: true);
        await using var db = fixture.CreateContext();
        var outsider = await TestData.CreateUserAsync(db);
        var tournaments = MultiplayerTest.Tournaments(db);
        Assert.Equal("TOURNAMENT_NOT_FOUND", (await tournaments.GetAsync(outsider, cup.Id)).Error?.Code);
        Assert.Equal("TOURNAMENT_NOT_FOUND", (await tournaments.RegisterAsync(outsider, cup.Id)).Error?.Code);
        Assert.Equal("TOURNAMENT_NOT_ORGANISER", (await tournaments.StartAsync(cup.Players[0], cup.Id, false)).Error?.Code);
        Assert.True((await tournaments.GetAsync(cup.Organiser, cup.Id)).Value!.CanManage);
        Assert.DoesNotContain(await tournaments.ListAsync(outsider), t => t.Id == cup.Id);
    }

    [Fact]
    public async Task Pair_pressing_play_simultaneously_get_one_reserved_two_player_room()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup); var view = await StartAsync(cup);
        var match = Assert.Single(Assert.Single(view.Rounds).Matches);
        var replies = await Task.WhenAll(cup.Players.Select(async player =>
        {
            await using var db = fixture.CreateContext();
            return await MultiplayerTest.Tournaments(db).PlayAsync(player, cup.Id, match.Id,
                new() { ProtocolVersion = 1, TransportSessionName = MultiplayerTest.NewTransportName(), RequestId = Guid.NewGuid().ToString() });
        }));
        Assert.All(replies, r => Assert.True(r.Succeeded, r.Error?.Code));
        Assert.Equal(replies[0].Value!.Session.Id, replies[1].Value!.Session.Id);
        Assert.Single(replies, r => r.Value!.YouHost);
        var room = replies[0].Value!.Session;
        Assert.Equal(2, room.MinPlayers); Assert.Equal(2, room.MaxPlayers);
        Assert.Equal(SessionVisibility.Private, room.Visibility);
        await using var check = fixture.CreateContext();
        var stranger = await TestData.CreateUserAsync(check);
        var joined = await MultiplayerTest.Sessions(check).JoinAsync(stranger, room.Id, new() { ProtocolVersion = 1 });
        Assert.False(joined.Succeeded);
    }

    [Fact]
    public async Task A_player_cannot_play_or_decide_another_pairing()
    {
        var cup = await CreateAsync(); await EnterAsync(cup); var view = await StartAsync(cup);
        var match = view.Rounds[0].Matches[0];
        var outsider = cup.Players.First(p => p != match.PlayerAUserId && p != match.PlayerBUserId);
        await using var db = fixture.CreateContext();
        var tournaments = MultiplayerTest.Tournaments(db);
        Assert.Equal("TOURNAMENT_MATCH_NOT_FOUND", (await tournaments.PlayAsync(outsider, cup.Id, match.Id, new() { ProtocolVersion = 1 })).Error?.Code);
        Assert.Equal("TOURNAMENT_NOT_ORGANISER", (await tournaments.DecideMatchAsync(outsider, cup.Id, match.Id,
            new() { WinnerUserId = match.PlayerAUserId, Reason = "Fake result" }, false)).Error?.Code);
    }

    [Fact]
    public async Task Two_advancers_settle_each_deadline_once()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup); await StartAsync(cup);
        await using (var db = fixture.CreateContext())
            await db.TournamentMatches.Where(m => m.TournamentId == cup.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.DeadlineAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        await Task.WhenAll(ReadAsync(cup), ReadAsync(cup));
        var view = await ReadAsync(cup);
        Assert.Equal(TournamentState.Completed, view.State);
        Assert.Equal("no_show", Assert.Single(view.Rounds[0].Matches).Outcome);
        Assert.DoesNotContain(view.Entrants, e => e.Placement == 1);
    }

    [Fact]
    public async Task A_blocked_pair_is_never_offered_a_room()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup);
        await using (var db = fixture.CreateContext())
        {
            db.PlayerBlocks.Add(new PlayerBlock { UserId = cup.Players[0], BlockedUserId = cup.Players[1], CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var view = await StartAsync(cup);
        Assert.Equal(TournamentState.Completed, view.State);
        Assert.Equal("not_played", Assert.Single(view.Rounds[0].Matches).Outcome);
        Assert.Null(view.MyMatch);
    }

    [Fact]
    public async Task Disqualification_is_audited_and_never_placed()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup); await StartAsync(cup);
        await using (var db = fixture.CreateContext())
            Assert.True((await MultiplayerTest.Tournaments(db).DisqualifyAsync(cup.Organiser, cup.Id, cup.Players[0], "Verified violation", true)).Succeeded);
        var view = await ReadAsync(cup);
        Assert.Null(view.Entrants.Single(e => e.UserId == cup.Players[0]).Placement);
        Assert.Equal(1, view.Entrants.Single(e => e.UserId == cup.Players[1]).Placement);
        await using var check = fixture.CreateContext();
        Assert.True(await check.AuditEvents.AnyAsync(a => a.TargetId == cup.Id.ToString() && a.Action == AuditActions.TournamentEntrantDisqualified));
    }

    [Fact]
    public async Task Swiss_keeps_everyone_playing_and_completes_the_scheduled_rounds()
    {
        var cup = await CreateAsync(4, TournamentFormat.Swiss); await EnterAsync(cup); var view = await StartAsync(cup);
        for (var round = 1; round <= 2; round++)
        {
            foreach (var match in view.Rounds.Single(r => r.Round == round).Matches)
            {
                await using var db = fixture.CreateContext();
                Assert.True((await MultiplayerTest.Tournaments(db).DecideMatchAsync(cup.Organiser, cup.Id, match.Id,
                    new() { WinnerUserId = match.PlayerAUserId, Reason = "Verified result" }, true)).Succeeded);
            }
            view = await ReadAsync(cup);
        }
        Assert.Equal(TournamentState.Completed, view.State);
        Assert.All(view.Entrants, e => Assert.Equal(2, e.Wins + e.Losses + e.Draws + e.Byes));
        Assert.Equal(new int?[] { 1, 2, 3, 4 }, view.Entrants.Select(e => e.Placement).Order());
    }

    [Fact]
    public async Task Cancellation_between_play_reads_and_its_lock_never_opens_a_room()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup); var view = await StartAsync(cup);
        var match = view.Rounds[0].Matches[0];
        var interleaving = new InterleavingInterceptor(async () =>
        {
            await using var other = fixture.CreateContext();
            Assert.True((await MultiplayerTest.Tournaments(other).CancelAsync(cup.Organiser, cup.Id, "Called off", true)).Succeeded);
        }, command => command.CommandText.Contains("HOLDLOCK", StringComparison.OrdinalIgnoreCase) || InterleavingInterceptor.IsAction(command.CommandText));
        await using var db = fixture.CreateInterleavedContext(interleaving);
        var played = await MultiplayerTest.Tournaments(db).PlayAsync(cup.Players[0], cup.Id, match.Id,
            new() { ProtocolVersion = 1, TransportSessionName = MultiplayerTest.NewTransportName() });
        Assert.True(interleaving.Fired);
        Assert.Equal("TOURNAMENT_MATCH_CLOSED", played.Error?.Code);
        Assert.False(await db.MultiplayerSessions.AnyAsync(s => s.TournamentMatchId == match.Id));
    }

    [Fact]
    public async Task A_block_added_after_seeding_prevents_the_pair_from_opening_a_room()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup); var view = await StartAsync(cup);
        var match = view.Rounds[0].Matches[0];
        await using (var db = fixture.CreateContext())
        {
            db.PlayerBlocks.Add(new PlayerBlock { UserId = cup.Players[0], BlockedUserId = cup.Players[1], CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var playing = fixture.CreateContext();
        var reply = await MultiplayerTest.Tournaments(playing).PlayAsync(cup.Players[0], cup.Id, match.Id,
            new() { ProtocolVersion = 1, TransportSessionName = MultiplayerTest.NewTransportName() });
        Assert.Equal("TOURNAMENT_MATCH_CLOSED", reply.Error?.Code);
        Assert.False(await playing.MultiplayerSessions.AnyAsync(s => s.TournamentMatchId == match.Id));
    }

    private async Task<Guid> FinishGameAsync(Cup cup, TournamentMatchDto match, bool tie)
    {
        await using var db = fixture.CreateContext();
        var mode = await db.GameModes.FirstAsync(m => m.Id == cup.Mode);
        mode.WinRuleJson = MatchWinRule.Build([((string?)MatchMetrics.Outcome, (string?)"higher")]).Rule!.ToJson();
        await db.SaveChangesAsync();
        var host = match.PlayerAUserId!.Value; var guest = match.PlayerBUserId!.Value;
        var opened = await MultiplayerTest.Tournaments(db).PlayAsync(host, cup.Id, match.Id,
            new() { ProtocolVersion = 1, TransportSessionName = MultiplayerTest.NewTransportName(), RequestId = Guid.NewGuid().ToString() });
        Assert.True(opened.Succeeded, opened.Error?.Code);
        var room = opened.Value!.Session.Id;
        var sessions = MultiplayerTest.Sessions(db);
        Assert.Equal(MultiplayerSessionState.Created, (await sessions.StartAsync(host, room, new())).Value!.State);
        Assert.True((await sessions.JoinAsync(guest, room, new() { ProtocolVersion = 1 })).Succeeded);
        Assert.Equal(MultiplayerSessionState.Running, (await sessions.StartAsync(host, room, new())).Value!.State);
        await db.MultiplayerSessions.Where(s => s.Id == room).ExecuteUpdateAsync(s => s.SetProperty(r => r.StartedAtUtc, DateTime.UtcNow.AddMinutes(-2)));
        await db.MultiplayerSessionPlayers.Where(p => p.SessionId == room).ExecuteUpdateAsync(s => s.SetProperty(p => p.JoinedAtUtc, DateTime.UtcNow.AddMinutes(-3)));
        foreach (var player in new[] { host, guest })
        {
            await using var playing = fixture.CreateContext();
            var runs = RunTestExtensions.CreateRunService(playing);
            var started = await runs.StartAsync(player, new StartRunRequest { GameId = cup.Game, SessionId = room, ModeKey = mode.ModeKey });
            Assert.True(started.Succeeded, started.Error?.Code);
            await playing.AgeRunAsync(started.Value!.RunId, 60);
            var result = await runs.SettleAsync(player, started.Value.RunId, new SubmitRunResultRequest
            { DurationMs = 1000, Outcome = (player == host || tie ? RunOutcome.Completed : RunOutcome.Failed).ToString() });
            Assert.True(result.Succeeded, result.Error?.Code);
        }
        Assert.True((await sessions.CloseAsync(host, room, new())).Succeeded);
        await using var deciding = fixture.CreateContext();
        var verdict = await MultiplayerTest.Results(deciding).GetAsync(host, room);
        Assert.True(verdict.Succeeded, verdict.Error?.Code);
        Assert.Equal(MatchResultStatus.Decided, verdict.Value!.State);
        return room;
    }

    [Fact]
    public async Task Real_settled_runs_advance_the_bracket_without_an_organiser()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup); var view = await StartAsync(cup);
        var match = view.Rounds[0].Matches[0];
        await FinishGameAsync(cup, match, tie: false);
        view = await ReadAsync(cup);
        Assert.Equal(TournamentState.Completed, view.State);
        Assert.Equal(match.PlayerAUserId, view.Rounds[0].Matches[0].WinnerUserId);
        Assert.Equal("played", view.Rounds[0].Matches[0].Outcome);
    }

    [Fact]
    public async Task A_tied_knockout_opens_a_fresh_room_and_a_fresh_retry_key()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup); var view = await StartAsync(cup);
        var room = await FinishGameAsync(cup, view.Rounds[0].Matches[0], tie: true);
        view = await ReadAsync(cup);
        var match = Assert.Single(view.Rounds[0].Matches);
        Assert.Equal(2, match.GameNumber); Assert.Equal(TournamentMatchState.Ready, match.State);
        Assert.Null(match.SessionId); Assert.False(match.PlayerACheckedIn); Assert.False(match.PlayerBCheckedIn);
        await using var db = fixture.CreateContext();
        var next = await MultiplayerTest.Tournaments(db).PlayAsync(match.PlayerAUserId!.Value, cup.Id, match.Id,
            new() { ProtocolVersion = 1, TransportSessionName = MultiplayerTest.NewTransportName(), RequestId = Guid.NewGuid().ToString() });
        Assert.True(next.Succeeded, next.Error?.Code);
        Assert.NotEqual(room, next.Value!.Session.Id);
    }

    [Fact]
    public async Task Unsupported_protocol_is_refused_even_after_an_opponent_opened_the_room()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup); var view = await StartAsync(cup);
        var match = view.Rounds[0].Matches[0];
        await using var db = fixture.CreateContext();
        var tournaments = MultiplayerTest.Tournaments(db);
        Assert.True((await tournaments.PlayAsync(cup.Players[0], cup.Id, match.Id,
            new() { ProtocolVersion = 1, TransportSessionName = MultiplayerTest.NewTransportName() })).Succeeded);
        Assert.Equal("PROTOCOL_VERSION_MISMATCH", (await tournaments.PlayAsync(cup.Players[1], cup.Id, match.Id,
            new() { ProtocolVersion = 99, TransportSessionName = MultiplayerTest.NewTransportName() })).Error?.Code);
    }

    private async Task<(Cup Cup, Guid Event)> PrizeFixtureAsync(bool limited)
    {
        var cup = await CreateAsync(4); await EnterAsync(cup); var view = await StartAsync(cup);
        while (view.State == TournamentState.Running)
        {
            foreach (var match in view.Rounds.Single(r => r.Round == view.CurrentRound).Matches.Where(m => m.State != TournamentMatchState.Completed))
            {
                await using var deciding = fixture.CreateContext();
                Assert.True((await MultiplayerTest.Tournaments(deciding).DecideMatchAsync(cup.Organiser, cup.Id, match.Id,
                    new() { WinnerUserId = match.PlayerAUserId, Reason = "Reviewed match" }, true)).Succeeded);
            }
            view = await ReadAsync(cup);
        }
        await using var db = fixture.CreateContext();
        var request = PlayTest.EventRequest(cup.Game, cup.Mode, LeaderboardMetrics.CorrectAnswers,
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(2));
        request.PrizeTiers = [PlayTest.RealWorldTier(limited ? 3 : 1, limited ? 3 : 1, "Test trophy", limited ? 1 : null)];
        var playEvent = await PlayTest.EventAdmin(db).CreateAsync(request, cup.Organiser);
        Assert.True(playEvent.Succeeded, string.Join("; ", playEvent.Errors));
        // Isolate the payout from automatic completion: the finished bracket above is real; binding
        // its test event here lets the test run the payout itself and interleave another payout.
        await db.Tournaments.Where(t => t.Id == cup.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.EventId, playEvent.Value!.EventId));
        return (cup, playEvent.Value!.EventId);
    }

    [Fact]
    public async Task A_tournament_prize_opens_one_review_claim_and_the_ladder_never_pays_it_again()
    {
        var (cup, eventId) = await PrizeFixtureAsync(limited: false);
        await using var db = fixture.CreateContext();
        var awards = PlayTest.Awards(db);
        await awards.AwardTournamentAsync(cup.Id); await awards.AwardTournamentAsync(cup.Id);
        var cycle = await db.PlayEvents.Where(e => e.Id == eventId).Select(e => e.CycleId).SingleAsync();
        await awards.OnCycleSettlingAsync(cycle);
        Assert.Single(await db.EventAwards.Where(a => a.EventId == eventId).ToListAsync());
        var claim = await db.PrizeClaims.SingleAsync(c => db.EventAwards.Any(a => a.Id == c.AwardId && a.EventId == eventId));
        Assert.Equal(PrizeClaimState.PendingReview, claim.State);
    }

    [Fact]
    public async Task Racing_tournament_payouts_never_promise_two_of_one_limited_prize()
    {
        var (cup, eventId) = await PrizeFixtureAsync(limited: true);
        var interleaving = new InterleavingInterceptor(async () =>
        {
            await using var other = fixture.CreateContext();
            await PlayTest.Awards(other).AwardTournamentAsync(cup.Id);
        });
        await using var db = fixture.CreateInterleavedContext(interleaving);
        await PlayTest.Awards(db).AwardTournamentAsync(cup.Id);
        Assert.True(interleaving.Fired);
        Assert.Single(await db.EventAwards.Where(a => a.EventId == eventId).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_event_tournament_creation_accepts_one_bracket_only()
    {
        var cup = await CreateAsync(2);
        await using var setup = fixture.CreateContext();
        var eventRequest = PlayTest.EventRequest(cup.Game, cup.Mode, LeaderboardMetrics.CorrectAnswers,
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(3));
        var playEvent = await PlayTest.EventAdmin(setup).CreateAsync(eventRequest, cup.Organiser);
        Assert.True(playEvent.Succeeded, string.Join("; ", playEvent.Errors));
        var eventId = playEvent.Value!.EventId;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creating = Enumerable.Range(0, 5).Select(async _ =>
        {
            await gate.Task;
            await using var db = fixture.CreateContext();
            return await MultiplayerTest.Tournaments(db).CreateAsync(cup.Organiser,
                new() { Title = "Event cup", GameId = cup.Game, ModeId = cup.Mode, EventId = eventId }, true);
        }).ToArray();
        gate.SetResult(); var results = await Task.WhenAll(creating);
        Assert.Single(results, r => r.Succeeded);
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal("TOURNAMENT_EVENT_UNSUITABLE", r.Error?.Code));
        Assert.Equal(1, await setup.Tournaments.CountAsync(t => t.EventId == eventId));
    }

    [Fact]
    public async Task Fifty_waiting_tournaments_do_not_starve_the_next_due_tournament()
    {
        var cup = await CreateAsync(2); await EnterAsync(cup); await StartAsync(cup);
        await using (var db = fixture.CreateContext())
        {
            var now = DateTime.UtcNow;
            for (var i = 0; i < 50; i++)
            {
                var id = Guid.NewGuid();
                db.Tournaments.Add(new Tournament
                {
                    Id = id, Title = "Waiting class cup", GameId = cup.Game, ModeId = cup.Mode,
                    Format = TournamentFormat.SingleElimination, State = TournamentState.Running,
                    MaxEntrants = 2, EntrantCount = 2, MatchMinutes = 10, CurrentRound = 1, RoundCount = 1,
                    CreatedByUserId = cup.Organiser, CreatedAtUtc = now, StartedAtUtc = now, AdvancedAtUtc = now.AddMinutes(-2)
                });
                for (var seed = 0; seed < 2; seed++)
                    db.TournamentEntries.Add(new TournamentEntry { TournamentId = id, UserId = cup.Players[seed],
                        State = TournamentEntryState.Entered, Seed = seed + 1, RegisteredAtUtc = now });
                db.TournamentMatches.Add(new TournamentMatch { Id = Guid.NewGuid(), TournamentId = id, Round = 1,
                    PlayerAUserId = cup.Players[0], PlayerBUserId = cup.Players[1], State = TournamentMatchState.Ready,
                    ReadyAtUtc = now, DeadlineAtUtc = now.AddMinutes(10) });
            }
            await db.SaveChangesAsync();
            await db.Tournaments.Where(t => t.Id == cup.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.AdvancedAtUtc, now.AddMinutes(-1)));
            await db.TournamentMatches.Where(m => m.TournamentId == cup.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.DeadlineAtUtc, now.AddMinutes(-1)));
        }
        for (var pass = 0; pass < 2; pass++)
        {
            await using var db = fixture.CreateContext();
            await MultiplayerTest.Tournaments(db).AdvanceDueAsync();
        }
        await using var check = fixture.CreateContext();
        Assert.Equal(TournamentState.Completed, (await check.Tournaments.SingleAsync(t => t.Id == cup.Id)).State);
    }
}
