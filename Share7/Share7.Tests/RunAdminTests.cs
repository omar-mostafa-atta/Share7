using Microsoft.EntityFrameworkCore;
using Share7.Application.Runs.Models;
using Share7.Application.Runs.Models.Admin;
using Share7.Domain.Multiplayer;
using Share7.Application.Rewards.Models;
using Share7.Domain.Rewards;
using Share7.Domain.Runs;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Tuning the economy without a deploy, reading the runs that tripped a bound, and the rules that
/// keep a currency people paid real money for out of reach of a farming script.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RunAdminTests
{
    private readonly SqlServerFixture _fixture;

    public RunAdminTests(SqlServerFixture fixture) => _fixture = fixture;

    // ---- valuation authoring ---------------------------------------------------------------

    [Fact]
    public async Task A_price_authored_through_the_admin_service_is_what_the_next_run_pays()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var admin = RunTestExtensions.CreateRunAdminService(context);

        var created = await admin.CreateValuationAsync(new CreatePickupValuationRequest
        {
            GameId = game.Id,
            PickupKind = "coin",
            CurrencyId = coins.Id,
            UnitValue = 3,
            MaxPerRun = 100
        });

        Assert.True(created.Succeeded);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 10));

        Assert.Equal(30, Assert.Single(settled.Value!.Rewards).Amount);

        // The whole point of the table: retuning is an edit, not a deploy and not a client release.
        await admin.UpdateValuationAsync(created.Value!.Id, new UpdatePickupValuationRequest
        {
            UnitValue = 6,
            MaxPerRun = 100,
            Enabled = true
        });

        var next = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var doubled = await runs.SettleAsync(userId, next.Value!.RunId, RunTestExtensions.Result(coins: 10));

        Assert.Equal(60, Assert.Single(doubled.Value!.Rewards).Amount);
    }

    [Fact]
    public async Task Retiring_a_price_stops_it_paying_without_deleting_the_history_it_explains()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var admin = RunTestExtensions.CreateRunAdminService(context);

        var created = await admin.CreateValuationAsync(new CreatePickupValuationRequest
        {
            GameId = game.Id, PickupKind = "coin", CurrencyId = coins.Id, UnitValue = 2, MaxPerRun = 100
        });

        var runs = RunTestExtensions.CreateRunService(context);
        var paid = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        await runs.SettleAsync(userId, paid.Value!.RunId, RunTestExtensions.Result(coins: 5));

        await admin.UpdateValuationAsync(created.Value!.Id, new UpdatePickupValuationRequest
        {
            UnitValue = 2, MaxPerRun = 100, Enabled = false
        });

        var after = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var unpaid = await runs.SettleAsync(userId, after.Value!.RunId, RunTestExtensions.Result(coins: 5));

        Assert.Empty(unpaid.Value!.Rewards);

        // The row survives, so the payout it already priced stays explicable. There is no delete.
        await using var check = _fixture.CreateContext();
        Assert.NotNull(await check.PickupValuations.FindAsync(created.Value.Id));
        Assert.Equal(10, Assert.Single(await check.PayoutsOfAsync(paid.Value.RunId)).NetAmount);
    }

    [Fact]
    public async Task A_second_price_for_the_same_game_kind_and_currency_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var admin = RunTestExtensions.CreateRunAdminService(context);

        var request = new CreatePickupValuationRequest
        {
            GameId = game.Id, PickupKind = "coin", CurrencyId = coins.Id, UnitValue = 1, MaxPerRun = 10
        };

        Assert.True((await admin.CreateValuationAsync(request)).Succeeded);

        var duplicate = await admin.CreateValuationAsync(request);

        Assert.False(duplicate.Succeeded);
        Assert.Equal("VALUATION_DUPLICATE", duplicate.Error?.Code);
    }

    [Fact]
    public async Task An_illegal_pickup_kind_is_refused_rather_than_stored_unmatchable()
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();

        var refused = await RunTestExtensions.CreateRunAdminService(context)
            .CreateValuationAsync(new CreatePickupValuationRequest
            {
                PickupKind = "Coin-Large", CurrencyId = coins.Id, UnitValue = 1, MaxPerRun = 10
            });

        Assert.False(refused.Succeeded);
        Assert.Equal("VALUATION_INVALID", refused.Error?.Code);
    }

    // ---- hard currency ---------------------------------------------------------------------

    /// <summary>Test 14.</summary>
    [Fact]
    public async Task A_hard_currency_valuation_without_a_daily_cap_is_refused_at_creation()
    {
        await using var context = _fixture.CreateContext();
        var gems = await context.CreateCappedCurrencyAsync(isHard: true, dailyEarnCap: 10);
        var admin = RunTestExtensions.CreateRunAdminService(context);

        var unbounded = await admin.CreateValuationAsync(new CreatePickupValuationRequest
        {
            PickupKind = "gem", CurrencyId = gems.Id, UnitValue = 1, MaxPerRun = 5
        });

        // Refused at creation, not clamped at settlement: a missing bound on something people paid
        // real money for is currency already in circulation by the time anybody notices, and unlike a
        // soft currency it can never be rebalanced downward.
        Assert.False(unbounded.Succeeded);
        Assert.Equal("VALUATION_INVALID", unbounded.Error?.Code);

        var bounded = await admin.CreateValuationAsync(new CreatePickupValuationRequest
        {
            PickupKind = "gem", CurrencyId = gems.Id, UnitValue = 1, MaxPerRun = 5, MaxPerDay = 5
        });

        Assert.True(bounded.Succeeded);
    }

    [Fact]
    public async Task A_hard_currency_valuation_cannot_have_its_daily_cap_cleared_later()
    {
        await using var context = _fixture.CreateContext();
        var gems = await context.CreateCappedCurrencyAsync(isHard: true, dailyEarnCap: 10);
        var admin = RunTestExtensions.CreateRunAdminService(context);

        var created = await admin.CreateValuationAsync(new CreatePickupValuationRequest
        {
            PickupKind = "gem", CurrencyId = gems.Id, UnitValue = 1, MaxPerRun = 5, MaxPerDay = 5
        });

        // Clearing it later is exactly as unbounded as never setting it.
        var cleared = await admin.UpdateValuationAsync(created.Value!.Id, new UpdatePickupValuationRequest
        {
            UnitValue = 1, MaxPerRun = 5, MaxPerDay = null, Enabled = true
        });

        Assert.False(cleared.Succeeded);
        Assert.Equal("VALUATION_INVALID", cleared.Error?.Code);
    }

    [Fact]
    public async Task An_every_time_rule_granting_a_hard_currency_needs_a_daily_limit()
    {
        await using var context = _fixture.CreateContext();
        var gems = await context.CreateCappedCurrencyAsync(isHard: true, dailyEarnCap: 100);
        var admin = new Share7.Infrastructure.Rewards.RewardAdminService(context);

        // Scoped to a game nothing else uses. RUN_SETTLED rules are global configuration on a shared
        // database, so a rule left matching every game pays out inside unrelated tests and makes them
        // pass -- or fail -- for the wrong reason. RewardServiceTests learned this first.
        var scope = Guid.NewGuid().ToString();

        var unbounded = await admin.CreateRuleAsync(new CreateRewardRuleRequest
        {
            Name = "gems per run",
            EventType = "RUN_SETTLED",
            ReferenceKey = scope,
            RepeatPolicy = "EVERY_TIME",
            Grants = [new RewardGrantRequest { CurrencyId = gems.Id, Amount = 5 }]
        });

        Assert.False(unbounded.Succeeded);
        Assert.Equal("REWARD_RULE_INVALID", unbounded.Error?.Code);

        var bounded = await admin.CreateRuleAsync(new CreateRewardRuleRequest
        {
            Name = "gems per run, bounded",
            EventType = "RUN_SETTLED",
            ReferenceKey = scope,
            RepeatPolicy = "EVERY_TIME",
            DailyLimit = 2,
            Grants = [new RewardGrantRequest { CurrencyId = gems.Id, Amount = 5 }]
        });

        Assert.True(bounded.Succeeded);
    }

    [Fact]
    public async Task A_once_rule_granting_a_hard_currency_needs_no_limit_because_it_already_has_one()
    {
        await using var context = _fixture.CreateContext();
        var gems = await context.CreateCappedCurrencyAsync(isHard: true, dailyEarnCap: 100);
        var scope = Guid.NewGuid().ToString();

        var once = await new Share7.Infrastructure.Rewards.RewardAdminService(context)
            .CreateRuleAsync(new CreateRewardRuleRequest
            {
                Name = "first ever run",
                EventType = "RUN_SETTLED",
                ReferenceKey = scope,
                RepeatPolicy = "ONCE",
                Grants = [new RewardGrantRequest { CurrencyId = gems.Id, Amount = 25 }]
            });

        // Once per account, ever. A daily limit would add nothing, and is refused on ONCE anyway.
        Assert.True(once.Succeeded);
    }

    [Fact]
    public async Task A_hard_currency_defaults_to_no_gameplay_source_at_all()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();

        var created = await new Share7.Infrastructure.Economy.CurrencyAdminService(
                context, new Share7.Infrastructure.Economy.WalletService(context))
            .CreateAsync(new Share7.Application.Economy.Models.CreateCurrencyRequest
            {
                Key = $"g{Guid.NewGuid():N}"[..12],
                Name = "Gems",
                IsHard = true
            });

        Assert.True(created.Succeeded);

        // Omitting the cap means zero, not "unlimited". An operator who forgets gets a currency that
        // cannot be farmed, rather than one that can be farmed without limit.
        Assert.Equal(0, created.Value!.DailyEarnCap);

        await context.CreateValuationAsync(
            created.Value.CurrencyId, kind: "gem", gameId: game.Id, unitValue: 10, maxPerRun: 5, maxPerDay: 5);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        var settled = await runs.SettleAsync(
            userId, started.Value!.RunId, RunTestExtensions.Result(coins: 5, kind: "gem"));

        // The gate: no gameplay path mints hard currency.
        Assert.True(settled.Succeeded);
        Assert.Empty(settled.Value!.Rewards);
        Assert.True(settled.Value.CapReached);
        Assert.Equal("daily_coin_limit", settled.Value.CapMessage);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.BalanceOfAsync(userId, created.Value.CurrencyId));
    }

    // ---- corroboration ---------------------------------------------------------------------

    /// <summary>Test 15.</summary>
    [Fact]
    public async Task A_run_naming_a_session_the_account_was_never_in_is_flagged()
    {
        await using var context = _fixture.CreateContext();
        var player = await TestData.CreateUserAsync(context);
        var stranger = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1);

        var sessionId = await SeatAsync(context, game.Id, player);
        var runs = RunTestExtensions.CreateRunService(context);

        var member = await runs.StartAsync(player, new StartRunRequest { GameId = game.Id, SessionId = sessionId });

        // Aged so the claimed minute of play is real elapsed time. Without it the duration clamp fires
        // correctly and flags the run, and this test would be asserting the wrong flag's absence.
        await context.AgeRunAsync(member.Value!.RunId, seconds: 120);
        await runs.SettleAsync(player, member.Value.RunId, RunTestExtensions.Result(coins: 5));

        var outsider = await runs.StartAsync(stranger, new StartRunRequest { GameId = game.Id, SessionId = sessionId });

        await context.AgeRunAsync(outsider.Value!.RunId, seconds: 120);
        var claimed = await runs.SettleAsync(stranger, outsider.Value.RunId, RunTestExtensions.Result(coins: 5));

        await using var check = _fixture.CreateContext();

        Assert.False((await check.RunOfAsync(member.Value.RunId)).IsFlagged);

        var flagged = await check.RunOfAsync(outsider.Value.RunId);
        Assert.True(flagged.IsFlagged);
        Assert.Contains("session_unverified", flagged.FlagReason);

        // Flagged, not refused. A session swept while a result sat in the offline queue looks
        // identical to a forged one, and the child who actually played must not lose their run.
        Assert.True(claimed.Succeeded);
        Assert.Equal(5, Assert.Single(claimed.Value!.Rewards).Amount);
    }

    // ---- review queue ----------------------------------------------------------------------

    [Fact]
    public async Task A_capped_run_reaches_the_review_queue_and_leaves_it_once_reviewed()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var reviewerId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 20);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });
        await runs.SettleAsync(userId, started.Value!.RunId, RunTestExtensions.Result(coins: 47));

        var admin = RunTestExtensions.CreateRunAdminService(context);

        var queued = await admin.GetFlaggedRunsAsync(take: 200);
        var mine = Assert.Single(queued, r => r.RunId == started.Value.RunId);

        // The queue has to answer "why did they only get 20?" without a second query.
        Assert.Contains("pickup_capped", mine.FlagReason);
        Assert.Equal(47, Assert.Single(mine.Collected).Count);

        var payout = Assert.Single(mine.Payouts);
        Assert.Equal(47, payout.GrossAmount);
        Assert.Equal(27, payout.CappedAmount);
        Assert.Equal(20, payout.NetAmount);

        var reviewed = await admin.ReviewRunAsync(
            started.Value.RunId, reviewerId, new ReviewRunRequest { Note = "bad clock, legitimate" });

        Assert.True(reviewed.Succeeded);
        Assert.Equal(reviewerId, reviewed.Value!.ReviewedByUserId);

        // **The flag stays.** It records what happened to the run; the review records a judgement
        // about it. Clearing it would tidy the queue at the cost of the payout being explicable.
        Assert.True(reviewed.Value.IsFlagged);
        Assert.Contains("pickup_capped", reviewed.Value.FlagReason);

        Assert.DoesNotContain(
            await admin.GetFlaggedRunsAsync(take: 200), r => r.RunId == started.Value.RunId);

        Assert.Contains(
            await admin.GetFlaggedRunsAsync(take: 200, includeReviewed: true),
            r => r.RunId == started.Value.RunId);
    }

    [Fact]
    public async Task A_clean_run_never_reaches_the_review_queue()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        await context.CreateValuationAsync(coins.Id, gameId: game.Id, unitValue: 1, maxPerRun: 500);

        var runs = RunTestExtensions.CreateRunService(context);
        var started = await runs.StartAsync(userId, new StartRunRequest { GameId = game.Id });

        // A minute of real elapsed time, so the claimed minute is not clamped. A clean run has to be
        // genuinely clean for this to be testing anything.
        await context.AgeRunAsync(started.Value!.RunId, seconds: 120);
        await runs.SettleAsync(userId, started.Value.RunId, RunTestExtensions.Result(coins: 12));

        Assert.DoesNotContain(
            await RunTestExtensions.CreateRunAdminService(context).GetFlaggedRunsAsync(take: 200),
            r => r.RunId == started.Value.RunId);
    }

    private static async Task<Guid> SeatAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        Guid gameId,
        Guid userId)
    {
        var now = DateTime.UtcNow;

        var session = new MultiplayerSession
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            HostUserId = userId,
            TransportSessionName = $"room_{Guid.NewGuid():N}"[..24],
            State = MultiplayerSessionState.Created,
            Visibility = SessionVisibility.Public,
            MaxPlayers = 2,
            MinPlayers = 1,
            CurrentPlayerCount = 1,
            ProtocolVersion = 1,
            CreatedAtUtc = now,
            LastHeartbeatAtUtc = now
        };

        context.MultiplayerSessions.Add(session);

        context.MultiplayerSessionPlayers.Add(new MultiplayerSessionPlayer
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Slot = 0,
            IsHost = true,
            Status = SessionPlayerStatus.Joined,
            JoinedAtUtc = now,
            LastSeenAtUtc = now
        });

        await context.SaveChangesAsync();
        return session.Id;
    }
}
