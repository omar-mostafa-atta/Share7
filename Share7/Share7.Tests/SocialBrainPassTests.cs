using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Social;
using Share7.Application.BrainPass;
using Share7.Domain.BrainPass;
using Share7.Domain.Constants;
using Share7.Domain.Leaderboards;
using Share7.Domain.Multiplayer;
using Share7.Domain.Organizations;
using Share7.Domain.Rewards;
using Share7.Domain.Social;
using Share7.Infrastructure.Audit;
using Share7.Infrastructure.BrainPass;
using Share7.Infrastructure.Commerce;
using Share7.Infrastructure.Economy;
using Share7.Infrastructure.Leaderboards;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Progression;
using Share7.Infrastructure.Rewards;
using Share7.Infrastructure.Social;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class SocialBrainPassTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Permanent_restriction_appeal_review_is_audited_and_decision_retries_cannot_change_terms()
    {
        await using var db = fixture.CreateContext(); var (a, b) = await SocialTest.ClassmatesAsync(db);
        var report = (await Safety(db).ReportAsync(a, new(b, null, ReportReason.Cheating, Guid.NewGuid().ToString("N")))).Value!;
        var decision = new ModerationDecisionRequest(ModerationState.Actioned, "verified_cheating", null, true);
        Assert.True((await Safety(db).DecideAsync(report.Id, decision)).Succeeded);
        Assert.True((await Safety(db).DecideAsync(report.Id, decision)).Succeeded);
        Assert.False((await Safety(db).DecideAsync(report.Id, decision with { RestrictDays = 7, PermanentRestriction = false })).Succeeded);
        var restriction = Assert.Single(await Safety(db).RestrictionsAsync(b)); Assert.Null(restriction.ExpiresAtUtc);
        Assert.True((await Safety(db).AppealAsync(b, restriction.Id, "context")).Succeeded);
        db.ChangeTracker.Clear();
        Assert.True((await Safety(db).ReviewAppealAsync(restriction.Id, false, "evidence_confirmed")).Succeeded);
        Assert.True((await Safety(db).ReviewAppealAsync(restriction.Id, false, "evidence_confirmed")).Succeeded);
        Assert.False((await Safety(db).ReviewAppealAsync(restriction.Id, true, "evidence_confirmed")).Succeeded);
        Assert.DoesNotContain(await Safety(db).AppealsAsync(), x => x.RestrictionId == restriction.Id);
        Assert.Equal("upheld:evidence_confirmed", Assert.Single(await Safety(db).RestrictionsAsync(b)).AppealResolutionCode);
        Assert.False((await SocialTest.Policy(db).CanInteractAsync(a, b, SocialAction.Invite)).Allowed);
        Assert.True((await Safety(db).RevokeAsync(restriction.Id, "later_evidence")).Succeeded);
        Assert.True((await SocialTest.Policy(db).CanInteractAsync(a, b, SocialAction.Invite)).Allowed);
    }

    [Fact]
    public async Task Inbox_is_recipient_scoped_strips_legacy_names_and_advances_over_hidden_rows()
    {
        await using var db = fixture.CreateContext(); var user = await TestData.CreateUserAsync(db); var other = await TestData.CreateUserAsync(db);
        var session = Guid.NewGuid(); var publisher = SocialTest.Publisher(db);
        publisher.Stage(user, "multiplayer.friend.requested", new { fromUserId = other, sessionId = session, fromDisplayName = "Private Name", email = "private@example.test", tier = "invalid" });
        publisher.Stage(other, "reward.granted", new { transactionId = Guid.NewGuid() }); await db.SaveChangesAsync();
        var inbox = new InboxService(db); var first = Assert.Single((await inbox.ReadAsync(user, 0)).Value!.Items);
        Assert.Equal(session, first.Payload.GetProperty("sessionId").GetGuid()); Assert.False(first.Payload.TryGetProperty("fromDisplayName", out _));
        Assert.False(first.Payload.TryGetProperty("email", out _)); Assert.False(first.Payload.TryGetProperty("tier", out _));
        Assert.False((await inbox.MarkReadAsync(other, first.EventId)).Succeeded);
        Assert.True((await inbox.MarkReadAsync(user, first.EventId)).Succeeded);
        Assert.True(Assert.Single((await inbox.ReadAsync(user, 0)).Value!.Items).Read);
        Assert.False((await inbox.PreferenceAsync(user, "reward", false)).Succeeded);
        for (var n = 0; n < 101; n++) publisher.Stage(user, "multiplayer.friend.requested", new { fromUserId = other });
        await db.SaveChangesAsync(); await Safety(db).MuteAsync(user, other, true);
        var filtered = (await inbox.ReadAsync(user, 0)).Value!;
        Assert.Empty(filtered.Items); Assert.NotNull(filtered.NextAfter);
        Assert.Empty((await inbox.ReadAsync(user, filtered.NextAfter!.Value)).Value!.Items);
    }

    [Fact]
    public async Task Season_publish_requires_reviewed_version_freezes_terms_and_rejects_overlap()
    {
        await using var db = fixture.CreateContext(); var currency = await db.CreateCurrencyAsync();
        var reward = await db.CreateRewardRuleAsync(RewardEventType.BrainPassTier, [new(currency.Id, 25)]);
        var start = DateTime.UtcNow.AddDays(100);
        var input = new BrainPassSeasonInput(Guid.NewGuid().ToString("N"), "Reviewed season", "موسم", start, start.AddDays(10), start.AddDays(15), null, null,
            [new("LESSONS_COMPLETED", 1, 1, 10, 10, 100)], [new(1, BrainPassTrack.Free, 50, reward.Id)]);
        var admin = new BrainPassAdminService(db, Audit(db)); var saved = (await admin.SaveAsync(null, input)).Value!;
        Assert.False((await admin.PublishAsync(saved.Id, saved.Version - 1)).Succeeded);
        Assert.True((await admin.PublishAsync(saved.Id, saved.Version)).Succeeded);
        Assert.True((await admin.PublishAsync(saved.Id, saved.Version)).Succeeded);
        Assert.False((await admin.SaveAsync(saved.Id, input with { ExpectedVersion = saved.Version + 1, NameEn = "Changed" })).Succeeded);
        var overlap = (await admin.SaveAsync(null, input with { Key = Guid.NewGuid().ToString("N") })).Value!;
        Assert.False((await admin.PublishAsync(overlap.Id, overlap.Version)).Succeeded);
        Assert.True((await admin.DisableAsync(saved.Id)).Succeeded);
    }

    private static AuditLog Audit(ApplicationDbContext db) => new(db, new SystemAuditActor());
    private static BrainPassService Pass(ApplicationDbContext db) => new(db,
        new RewardService(db, new WalletService(db), new LevelService(db), new EntitlementService(db), SocialTest.Publisher(db)),
        SocialTest.Publisher(db), new StubLanguageService(LanguageIds.English));
    private static GamingProfileService Profiles(ApplicationDbContext db) => new(db, SocialTest.Policy(db), new BlockList(db),
        new DisplayNameService(db, Options.Create(new Share7.Application.Leaderboards.Models.LeaderboardOptions())),
        SocialTest.Presence(db), new StubLanguageService(LanguageIds.English));
    private static SocialSafetyService Safety(ApplicationDbContext db) => new(db, Profiles(db), Audit(db), SocialTest.Publisher(db));
    private static PlayerTeamService Teams(ApplicationDbContext db) => new(db, SocialTest.Policy(db),
        new DisplayNameService(db, Options.Create(new Share7.Application.Leaderboards.Models.LeaderboardOptions())), new BlockList(db), SocialTest.Publisher(db));

    private async Task<(Guid User, Guid Season, Guid Currency, Guid Game)> SeasonAsync(int cap = 100, string metric = "LESSON_BEST_PERCENT")
    {
        await using var db = fixture.CreateContext();
        var user = await TestData.CreateUserAsync(db); var path = await TestData.CreateCurriculumPathAsync(db);
        var currency = await db.CreateCurrencyAsync();
        var reward = await db.CreateRewardRuleAsync(RewardEventType.BrainPassTier, [new(currency.Id, 25)]);
        var season = new BrainPassSeason { Id = Guid.NewGuid(), Key = Guid.NewGuid().ToString("N"), NameEn = "Test season", NameAr = "موسم",
            StartsAtUtc = DateTime.UtcNow.AddDays(-2), EndsAtUtc = DateTime.UtcNow.AddDays(2), ClaimUntilUtc = DateTime.UtcNow.AddDays(3),
            State = BrainPassState.Published, CreatedAtUtc = DateTime.UtcNow, Version = 1 };
        db.BrainPassSeasons.Add(season);
        db.BrainPassTiers.Add(new() { SeasonId = season.Id, Number = 1, Track = BrainPassTrack.Free, RequiredXp = 50, RewardRuleId = reward.Id });
        db.BrainPassXpRules.Add(new() { SeasonId = season.Id, Metric = metric, MinimumValue = metric == "RUN_SECONDS" ? 60 : 1,
            UnitValue = metric == "RUN_SECONDS" ? 60 : 1, XpPerUnit = 1, MaxSourceXp = 100, DailyCap = cap });
        await db.SaveChangesAsync(); return (user, season.Id, currency.Id, path.GameId);
    }
    private static GameResult Result(Guid user, Guid game, long value, Guid? source = null, string metric = "LESSON_BEST_PERCENT",
        bool flagged = false, DateTime? at = null) => new() { Id = Guid.NewGuid(), UserId = user, GameId = game,
        Metric = metric, Value = value, SourceId = source ?? Guid.NewGuid(), SourceType = metric.StartsWith("LESSON") ? GameResultSource.Attempt : GameResultSource.Session,
        IsFlagged = flagged, OccurredAtUtc = at ?? DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow, RequestId = Guid.NewGuid().ToString("N") };

    [Fact]
    public async Task Source_improvements_count_once_and_flagged_results_never_earn()
    {
        var seed = await SeasonAsync(); var source = Guid.NewGuid();
        await using var db = fixture.CreateContext();
        db.GameResults.AddRange(Result(seed.User, seed.Game, 40, source), Result(seed.User, seed.Game, 80, source),
            Result(seed.User, seed.Game, 80, source), Result(seed.User, seed.Game, 100, flagged: true));
        await db.SaveChangesAsync();
        Assert.Equal(80, (await Pass(db).ReadAsync(seed.User, seed.Season)).Value!.Xp);
        Assert.Equal(80, (await Pass(db).ReadAsync(seed.User, seed.Season)).Value!.Xp);
        Assert.Equal(4, await db.BrainPassCredits.CountAsync(c => c.UserId == seed.User && c.SeasonId == seed.Season));
    }

    [Fact]
    public async Task Daily_cap_is_shared_across_sources_and_consumed_progress_cannot_replay_next_day()
    {
        var seed = await SeasonAsync(cap: 60); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await using var db = fixture.CreateContext();
        var yesterday = DateTime.UtcNow.Date.AddDays(-1).AddHours(12);
        db.GameResults.AddRange(Result(seed.User, seed.Game, 100, a, at: yesterday), Result(seed.User, seed.Game, 100, b, at: yesterday));
        await db.SaveChangesAsync(); Assert.Equal(60, (await Pass(db).ReadAsync(seed.User, seed.Season)).Value!.Xp);
        db.GameResults.AddRange(Result(seed.User, seed.Game, 100, a), Result(seed.User, seed.Game, 100, b));
        await db.SaveChangesAsync(); Assert.Equal(60, (await Pass(db).ReadAsync(seed.User, seed.Season)).Value!.Xp);
        db.GameResults.Add(Result(seed.User, seed.Game, 100)); await db.SaveChangesAsync();
        Assert.Equal(120, (await Pass(db).ReadAsync(seed.User, seed.Season)).Value!.Xp);
    }

    [Fact]
    public async Task A_late_lower_sequence_commit_is_not_lost_by_projection()
    {
        var seed = await SeasonAsync();
        await using var low = fixture.CreateContext(); await using var pending = await low.Database.BeginTransactionAsync();
        low.GameResults.Add(Result(seed.User, seed.Game, 20)); await low.SaveChangesAsync();
        await using (var high = fixture.CreateContext()) { high.GameResults.Add(Result(seed.User, seed.Game, 30)); await high.SaveChangesAsync(); }
        // Avoid a READ COMMITTED scan blocking on the uncommitted row: commit the lower sequence
        // after recording a credit for the later result, as a worker using an earlier snapshot can.
        await using (var high = fixture.CreateContext())
        {
            var later = await high.GameResults.AsNoTracking().OrderByDescending(r => r.Sequence).FirstAsync();
            high.BrainPassCredits.Add(new() { SeasonId = seed.Season, UserId = seed.User, ResultId = later.Id, Xp = 30, CreatedAtUtc = DateTime.UtcNow });
            high.BrainPassProgress.Add(new() { SeasonId = seed.Season, UserId = seed.User, Xp = 30, UpdatedAtUtc = DateTime.UtcNow });
            await high.SaveChangesAsync();
        }
        await pending.CommitAsync();
        await using var db = fixture.CreateContext(); Assert.Equal(50, (await Pass(db).ReadAsync(seed.User, seed.Season)).Value!.Xp);
    }

    [Fact]
    public async Task Parallel_claims_pay_once_and_recover_after_disable()
    {
        var seed = await SeasonAsync();
        await using (var db = fixture.CreateContext()) { db.GameResults.Add(Result(seed.User, seed.Game, 80)); await db.SaveChangesAsync(); }
        var claims = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = fixture.CreateContext(); return await Pass(db).ClaimAsync(seed.User, seed.Season, 1, BrainPassTrack.Free);
        }));
        Assert.All(claims, c => Assert.True(c.Succeeded, c.Error?.Code));
        Assert.Single(claims, c => !c.Value!.Replayed);
        await using var check = fixture.CreateContext();
        Assert.Equal(25, await check.BalanceOfAsync(seed.User, seed.Currency));
        Assert.Equal(1, await check.BrainPassClaims.CountAsync(c => c.SeasonId == seed.Season && c.UserId == seed.User));
        await new BrainPassAdminService(check, Audit(check)).DisableAsync(seed.Season);
        Assert.True((await Pass(check).ClaimAsync(seed.User, seed.Season, 1, BrainPassTrack.Free)).Value!.Replayed);
    }

    [Fact]
    public async Task Outside_window_and_unverified_run_results_do_not_earn()
    {
        var seed = await SeasonAsync(metric: "RUN_SECONDS");
        await using var db = fixture.CreateContext();
        db.GameResults.AddRange(Result(seed.User, seed.Game, 3600, metric: "RUN_SECONDS"),
            Result(seed.User, seed.Game, 3600, metric: "RUN_SECONDS", at: DateTime.UtcNow.AddDays(-5)));
        await db.SaveChangesAsync(); Assert.Equal(0, (await Pass(db).ReadAsync(seed.User, seed.Season)).Value!.Xp);
        Assert.Equal(1, await db.BrainPassCredits.CountAsync(c => c.SeasonId == seed.Season && c.UserId == seed.User));
    }

    [Fact]
    public async Task Profiles_obey_classmate_privacy_and_never_return_login_identifiers()
    {
        await using var db = fixture.CreateContext(); var (a, b) = await SocialTest.ClassmatesAsync(db); var stranger = await TestData.CreateUserAsync(db);
        Assert.False((await Profiles(db).ReadAsync(stranger, b)).Succeeded);
        var dto = (await Profiles(db).ReadAsync(a, b)).Value!;
        Assert.NotNull(dto); Assert.NotEqual((await db.Users.FindAsync(b))!.UserName, dto.DisplayName);
        var text = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("example.test", text); Assert.DoesNotContain("GradeId", text); Assert.DoesNotContain("Age", text);
        await Safety(db).SetPrivacyAsync(b, new(SocialVisibility.Nobody, SocialVisibility.Nobody, SocialVisibility.Nobody,
            SocialVisibility.Nobody, SocialVisibility.Nobody, false));
        Assert.False((await Profiles(db).ReadAsync(a, b)).Succeeded);
        Assert.True((await Profiles(db).ReadAsync(b, b)).Succeeded);
        Assert.Empty(await SocialTest.Policy(db).ConnectionsAsync(a, SocialAction.Invite));
    }

    [Fact]
    public async Task Guardian_switch_requires_own_verified_link_and_preserves_other_scopes()
    {
        await using var db = fixture.CreateContext(); var guardian = await TestData.CreateUserAsync(db);
        var learner = await TestData.CreateUserAsync(db); var stranger = await TestData.CreateUserAsync(db);
        var link = new GuardianLink { Id = Guid.NewGuid(), GuardianUserId = guardian, LearnerUserId = learner,
            ConsentScope = GuardianConsentScope.ViewProgress, CreatedAtUtc = DateTime.UtcNow };
        db.GuardianLinks.Add(link); await db.SaveChangesAsync();
        var service = new GuardianSocialConsentService(db, Audit(db));
        Assert.False((await service.SetAsync(guardian, link.Id, true)).Succeeded);
        link.VerifiedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
        Assert.False((await service.SetAsync(stranger, link.Id, true)).Succeeded);
        Assert.True((await service.SetAsync(guardian, link.Id, true)).Succeeded);
        Assert.True(link.Permits(GuardianConsentScope.ViewProgress)); Assert.True(link.Permits(GuardianConsentScope.SocialPlay));
        Assert.True((await service.SetAsync(guardian, link.Id, false)).Succeeded);
        Assert.True(link.Permits(GuardianConsentScope.ViewProgress)); Assert.False(link.Permits(GuardianConsentScope.SocialPlay));
    }

    [Fact]
    public async Task Report_decision_restricts_social_actions_and_appeal_has_a_real_staff_reader()
    {
        await using var db = fixture.CreateContext(); var (a, b) = await SocialTest.ClassmatesAsync(db);
        var request = new ReportRequest(b, null, ReportReason.UnsafeBehaviour, Guid.NewGuid().ToString("N"));
        var receipt = await Safety(db).ReportAsync(a, request); Assert.True(receipt.Succeeded);
        Assert.Equal(receipt.Value!.Id, (await Safety(db).ReportAsync(a, request)).Value!.Id);
        Assert.Contains((await Safety(db).CasesAsync(0, ModerationState.Open)).Items, c => c.Id == receipt.Value.Id);
        Assert.True((await Safety(db).DecideAsync(receipt.Value.Id, new(ModerationState.Actioned, "unsafe_behaviour", 7))).Succeeded);
        Assert.False((await SocialTest.Policy(db).CanInteractAsync(a, b, SocialAction.Invite)).Allowed);
        var restriction = Assert.Single(await Safety(db).RestrictionsAsync(b));
        Assert.True((await Safety(db).AppealAsync(b, restriction.Id, "mistake")).Succeeded);
        Assert.Contains(await Safety(db).AppealsAsync(), r => r.RestrictionId == restriction.Id);
        Assert.True((await Safety(db).RevokeAsync(restriction.Id, "reviewed")).Succeeded);
        Assert.True((await SocialTest.Policy(db).CanInteractAsync(a, b, SocialAction.Invite)).Allowed);
    }

    [Fact]
    public async Task Archive_removes_full_session_keeps_minimal_private_history_and_expires_it()
    {
        await using var db = fixture.CreateContext(); var host = await TestData.CreateUserAsync(db); var stranger = await TestData.CreateUserAsync(db);
        var path = await TestData.CreateCurriculumPathAsync(db);
        var created = await MultiplayerTest.Sessions(db).CreateAsync(host, MultiplayerTest.CreateRequest(path.GameId)); Assert.True(created.Succeeded);
        var id = created.Value!.Id;
        await db.MultiplayerSessions.Where(s => s.Id == id).ExecuteUpdateAsync(set => set.SetProperty(s => s.State, MultiplayerSessionState.Closed)
            .SetProperty(s => s.EndedAtUtc, DateTime.UtcNow.AddDays(-91)));
        var archive = new SessionArchiveService(db, Audit(db)); await archive.SweepAsync();
        Assert.False(await db.MultiplayerSessions.AnyAsync(s => s.Id == id));
        Assert.True((await archive.ReadAsync(host, id)).Succeeded); Assert.False((await archive.ReadAsync(stranger, id)).Succeeded);
        Assert.True((await archive.ReadAsync(stranger, id, true)).Succeeded);
        var text = System.Text.Json.JsonSerializer.Serialize((await archive.ReadAsync(host, id)).Value);
        Assert.DoesNotContain("Transport", text); Assert.DoesNotContain("JoinCode", text);
        await db.SessionArchives.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.ExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        Assert.False((await archive.ReadAsync(host, id)).Succeeded); await archive.SweepAsync();
        Assert.False(await db.SessionArchiveParticipants.AnyAsync(p => p.SessionId == id));
    }

    [Fact]
    public async Task Private_team_requires_acceptance_owner_management_and_connection_consent()
    {
        await using var db = fixture.CreateContext(); var (a, b) = await SocialTest.ClassmatesAsync(db); var stranger = await TestData.CreateUserAsync(db);
        var key = Guid.NewGuid().ToString("N"); var created = await Teams(db).CreateAsync(a, key); Assert.True(created.Succeeded);
        var team = created.Value!.Id; Assert.Equal(team, (await Teams(db).CreateAsync(a, key)).Value!.Id);
        Assert.False((await Teams(db).InviteAsync(a, team, stranger)).Succeeded);
        Assert.True((await Teams(db).InviteAsync(a, team, b)).Succeeded);
        Assert.False((await Teams(db).ListAsync(b)).Single().Members.Single(m => m.UserId == b).Accepted);
        Assert.True((await Teams(db).AcceptAsync(b, team)).Succeeded);
        Assert.False((await Teams(db).DisbandAsync(b, team)).Succeeded);
        Assert.True((await Teams(db).RemoveAsync(b, team, b)).Succeeded); Assert.Empty(await Teams(db).ListAsync(b));
        Assert.True((await Teams(db).DisbandAsync(a, team)).Succeeded);
    }

    [Fact]
    public async Task Racing_team_invitations_cannot_overfill_the_private_roster()
    {
        Guid owner; Guid team; Guid[] targets;
        await using (var db = fixture.CreateContext())
        {
            owner = await TestData.CreateUserAsync(db); var list = new List<Guid>();
            for (var i = 0; i < 12; i++) list.Add(await TestData.CreateUserAsync(db)); targets = list.ToArray();
            await SocialTest.ClassAsync(db, [owner, .. targets]); team = (await Teams(db).CreateAsync(owner, Guid.NewGuid().ToString("N"))).Value!.Id;
        }
        var invites = await Task.WhenAll(targets.Select(async target =>
        { await using var db = fixture.CreateContext(); return await Teams(db).InviteAsync(owner, team, target); }));
        Assert.Equal(PlayerTeamService.Capacity - 1, invites.Count(i => i.Succeeded));
        await using var check = fixture.CreateContext(); Assert.Equal(PlayerTeamService.Capacity, await check.PlayerTeamMembers.CountAsync(m => m.TeamId == team));
    }

    [Fact]
    public async Task Public_browser_hides_private_full_and_blocked_rooms_and_uses_protocol_gate()
    {
        await using var db = fixture.CreateContext(); var host = await TestData.CreateUserAsync(db); var reader = await TestData.CreateUserAsync(db);
        var path = await TestData.CreateCurriculumPathAsync(db);
        var session = (await MultiplayerTest.Sessions(db).CreateAsync(host, MultiplayerTest.CreateRequest(path.GameId))).Value!;
        await db.MultiplayerSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, MultiplayerSessionState.Created));
        var directory = new PublicRoomDirectory(db, Options.Create(MultiplayerTest.Options()));
        Assert.Contains((await directory.ReadAsync(reader, 1, gameId: path.GameId)).Value!.Items, r => r.SessionId == session.Id);
        Assert.False((await directory.ReadAsync(reader, 999)).Succeeded);
        db.PlayerBlocks.Add(new() { UserId = reader, BlockedUserId = host, CreatedAtUtc = DateTime.UtcNow }); await db.SaveChangesAsync();
        Assert.DoesNotContain((await directory.ReadAsync(reader, 1, gameId: path.GameId)).Value!.Items, r => r.SessionId == session.Id);
        await db.PlayerBlocks.Where(b => b.UserId == reader).ExecuteDeleteAsync();
        await db.MultiplayerSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Visibility, SessionVisibility.Private));
        Assert.DoesNotContain((await directory.ReadAsync(reader, 1, gameId: path.GameId)).Value!.Items, r => r.SessionId == session.Id);
    }

    [Fact]
    public async Task Observer_capability_is_opt_in_consent_gated_and_never_allocates_a_player_seat()
    {
        await using var db = fixture.CreateContext(); var (host, watcher) = await SocialTest.ClassmatesAsync(db);
        var stranger = await TestData.CreateUserAsync(db); var path = await TestData.CreateCurriculumPathAsync(db);
        var session = (await MultiplayerTest.Sessions(db).CreateAsync(host, MultiplayerTest.CreateRequest(path.GameId))).Value!;
        await db.MultiplayerSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.State, MultiplayerSessionState.Created));
        var service = new SessionObserverService(db, SocialTest.Policy(db), Audit(db));
        db.ChangeTracker.Clear();
        Assert.False((await service.ConfigureAsync(host, session.Id, true)).Succeeded);
        Assert.True((await service.SetCapabilityAsync(new(path.GameId, 1, 1, 2, true))).Succeeded);
        var configured = await service.ConfigureAsync(host, session.Id, true);
        Assert.True(configured.Succeeded, string.Join("; ", configured.Errors));
        Assert.False((await service.JoinAsync(stranger, session.Id, 1)).Succeeded);
        Assert.True((await service.JoinAsync(watcher, session.Id, 1)).Succeeded);
        Assert.False(await db.MultiplayerSessionPlayers.AnyAsync(p => p.SessionId == session.Id && p.UserId == watcher));
        Assert.Equal(1, await db.MultiplayerSessions.Where(s => s.Id == session.Id).Select(s => s.CurrentPlayerCount).SingleAsync());
        Assert.Contains(watcher, (await service.RosterAsync(host, session.Id)).Value!.UserIds);
        Assert.False((await service.RosterAsync(watcher, session.Id)).Succeeded);
        Assert.False((await MultiplayerTest.Sessions(db).GetAsync(watcher, session.Id)).Succeeded);
        await service.SetCapabilityAsync(new(path.GameId, 1, 1, 2, false));
        Assert.Empty((await service.RosterAsync(host, session.Id)).Value!.UserIds);
    }
}
