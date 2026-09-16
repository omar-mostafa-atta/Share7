using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Play.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Play;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// What happens when a competition ends.
/// <para>
/// **The settlement job is retried by design**, so the property every test here is really about is
/// that running it twice pays once. A child paid twice for first place is a defect nobody reports;
/// a child paid once, correctly, is the entire point of the feature.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class EventPrizeSettlementTests
{
    private readonly SqlServerFixture _fixture;

    public EventPrizeSettlementTests(SqlServerFixture fixture) => _fixture = fixture;

    /// <summary>
    /// An event running right now, with whatever prize table the test is about.
    /// </summary>
    private static async Task<PlayEventAdminDto> OpenEventAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        Guid gameId,
        Guid modeId,
        params SaveEventPrizeTierRequest[] tiers)
    {
        var request = PlayTest.EventRequest(
            gameId, modeId, LeaderboardMetrics.CorrectAnswers,
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));

        request.PrizeTiers = [.. tiers];

        var created = await PlayTest.EventAdmin(context).CreateAsync(request, Guid.NewGuid());

        Assert.True(created.Succeeded, string.Join("; ", created.Errors));

        return created.Value!;
    }

    /// <summary>Projects the event's results, closes its cycle, and settles it — what the job does.</summary>
    private async Task SettleAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context, PlayEventAdminDto playEvent)
    {
        await LeaderboardTestExtensions.CreateProjector(context).ProjectPendingAsync(int.MaxValue);

        var cycle = await context.LeaderboardCycles.FirstAsync(c => c.Id == playEvent.CycleId);
        cycle.State = LeaderboardCycleState.Closed;
        cycle.ClosedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var settlement = LeaderboardTestExtensions.CreateSettlement(
            context, options: null, PlayTest.Awards(context));

        var result = await settlement.SettleAsync(playEvent.CycleId);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task Creating_an_event_creates_its_ladder_in_the_same_breath()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var created = await OpenEventAsync(context, game.Id, mode.Id);

        await using var check = _fixture.CreateContext();
        var board = await check.LeaderboardBoards.FirstAsync(b => b.Id == created.BoardId);
        var cycle = await check.LeaderboardCycles.FirstAsync(c => c.Id == created.CycleId);

        // The board is the event's own, scoped to its mode, and the window lives on the cycle — there
        // are deliberately no start and end columns on the event row to disagree with it.
        Assert.Equal(created.EventId, board.EventId);
        Assert.Equal(mode.Id, board.ModeId);
        Assert.Equal(LeaderboardPeriod.Event, board.Period);
        Assert.Equal(LeaderboardCycleState.Open, cycle.State);
        Assert.Equal(created.StartsAtUtc, cycle.StartsAtUtc);
    }

    [Fact]
    public async Task The_winner_is_paid_once_however_many_times_settlement_runs()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var playEvent = await OpenEventAsync(
            context, game.Id, mode.Id, PlayTest.CoinTier(1, 1, coins.Key, 500));

        await context.AddResultAsync(
            userId, game.Id, LeaderboardMetrics.CorrectAnswers, 9, modeId: mode.Id, eventId: playEvent.EventId);

        await SettleAsync(context, playEvent);

        // The retry: the job table delivers at-least-once, and a shared host can kill a worker
        // mid-payout, so this is the ordinary path rather than an edge case.
        await SettleAsync(context, playEvent);

        await using var check = _fixture.CreateContext();

        var award = Assert.Single(await check.EventAwards.Where(a => a.EventId == playEvent.EventId).ToListAsync());
        Assert.Equal(1, award.FinalRank);
        Assert.Equal(EventAwardState.Granted, award.State);

        var balance = await check.UserCurrencyBalances
            .Where(b => b.UserId == userId && b.CurrencyId == coins.Id)
            .Select(b => b.Amount)
            .FirstAsync();

        Assert.Equal(500, balance);
    }

    [Fact]
    public async Task A_placing_collects_the_narrowest_tier_that_covers_it_and_no_other()
    {
        await using var context = _fixture.CreateContext();
        var winner = await TestData.CreateUserAsync(context);
        var second = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var playEvent = await OpenEventAsync(
            context, game.Id, mode.Id,
            PlayTest.CoinTier(1, 1, coins.Key, 1000),
            PlayTest.CoinTier(2, 10, coins.Key, 100));

        await context.AddResultAsync(
            winner, game.Id, LeaderboardMetrics.CorrectAnswers, 20, modeId: mode.Id, eventId: playEvent.EventId);
        await context.AddResultAsync(
            second, game.Id, LeaderboardMetrics.CorrectAnswers, 5, modeId: mode.Id, eventId: playEvent.EventId);

        await SettleAsync(context, playEvent);

        await using var check = _fixture.CreateContext();

        // First place gets 1000 and *not* 1000 + 100. An event's prize table is a promise written by
        // an operator, and paying every band a rank falls into would be a surprise nobody authored.
        Assert.Equal(1000, await BalanceAsync(check, winner, coins.Id));
        Assert.Equal(100, await BalanceAsync(check, second, coins.Id));
    }

    [Fact]
    public async Task A_real_world_prize_opens_a_claim_rather_than_paying_anything()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var playEvent = await OpenEventAsync(
            context, game.Id, mode.Id, PlayTest.RealWorldTier(1, 1, "A tablet"));

        await context.AddResultAsync(
            userId, game.Id, LeaderboardMetrics.CorrectAnswers, 12, modeId: mode.Id, eventId: playEvent.EventId);

        await SettleAsync(context, playEvent);

        await using var check = _fixture.CreateContext();

        var award = Assert.Single(await check.EventAwards.Where(a => a.EventId == playEvent.EventId).ToListAsync());
        Assert.Equal(EventAwardState.AwaitingClaim, award.State);

        var claim = Assert.Single(await check.PrizeClaims.Where(c => c.AwardId == award.Id).ToListAsync());
        Assert.Equal(PrizeClaimState.PendingReview, claim.State);

        // A deadline, so an unanswered claim becomes a fact rather than an open obligation. Thirty
        // days is the authored default.
        Assert.True(claim.ExpiresAtUtc > DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task A_limited_prize_runs_out_rather_than_being_promised_twice()
    {
        await using var context = _fixture.CreateContext();
        var first = await TestData.CreateUserAsync(context);
        var second = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        // One physical prize spanning the top two places.
        var playEvent = await OpenEventAsync(
            context, game.Id, mode.Id, PlayTest.RealWorldTier(1, 2, "A bike", quantity: 1));

        await context.AddResultAsync(
            first, game.Id, LeaderboardMetrics.CorrectAnswers, 30, modeId: mode.Id, eventId: playEvent.EventId);
        await context.AddResultAsync(
            second, game.Id, LeaderboardMetrics.CorrectAnswers, 10, modeId: mode.Id, eventId: playEvent.EventId);

        await SettleAsync(context, playEvent);

        await using var check = _fixture.CreateContext();
        var awards = await check.EventAwards.Where(a => a.EventId == playEvent.EventId).ToListAsync();

        var awarded = Assert.Single(awards);
        Assert.Equal(first, awarded.UserId);
    }

    [Fact]
    public async Task A_cancelled_event_pays_nobody_even_after_its_cycle_settles()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var playEvent = await OpenEventAsync(
            context, game.Id, mode.Id, PlayTest.CoinTier(1, 3, coins.Key, 250));

        await context.AddResultAsync(
            userId, game.Id, LeaderboardMetrics.CorrectAnswers, 7, modeId: mode.Id, eventId: playEvent.EventId);

        var cancelled = await PlayTest.EventAdmin(context).CancelAsync(playEvent.EventId, "Content problem");
        Assert.True(cancelled.Succeeded);

        await SettleAsync(context, playEvent);

        await using var check = _fixture.CreateContext();

        Assert.Equal(0, await check.EventAwards.CountAsync(a => a.EventId == playEvent.EventId));
        Assert.Equal(0, await BalanceAsync(check, userId, coins.Id));

        // The standing itself survives: the child played, and their placing is history rather than a
        // promise. Only the prize is withheld.
        Assert.NotEqual(0, await check.LeaderboardSettlements.CountAsync(s => s.CycleId == playEvent.CycleId));
    }

    [Fact]
    public async Task An_event_with_a_real_world_prize_cannot_charge_for_entry()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);
        var product = await context.CreateProductAsync();

        var request = PlayTest.EventRequest(
            game.Id, mode.Id, LeaderboardMetrics.CorrectAnswers,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

        request.EntryProductId = product.Id;
        request.PrizeTiers = [PlayTest.RealWorldTier(1, 1, "500 EGP")];

        var created = await PlayTest.EventAdmin(context).CreateAsync(request, Guid.NewGuid());

        // A prize of real value plus a price of entry is a paid competition, which is a different
        // legal object in most of the world and not one this platform offers to children.
        Assert.False(created.Succeeded);
        Assert.Equal(ApiErrors.PlayEventInvalid.Code, created.Error?.Code);
    }

    [Fact]
    public async Task Overlapping_prize_tiers_are_refused_at_authoring()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var request = PlayTest.EventRequest(
            game.Id, mode.Id, LeaderboardMetrics.CorrectAnswers,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

        request.PrizeTiers =
        [
            PlayTest.CoinTier(1, 5, coins.Key, 100),
            PlayTest.CoinTier(3, 10, coins.Key, 50)
        ];

        var created = await PlayTest.EventAdmin(context).CreateAsync(request, Guid.NewGuid());

        Assert.False(created.Succeeded);
    }

    [Fact]
    public async Task A_running_events_prize_table_cannot_be_rewritten()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var playEvent = await OpenEventAsync(
            context, game.Id, mode.Id, PlayTest.CoinTier(1, 1, coins.Key, 500));

        var request = PlayTest.EventRequest(
            game.Id, mode.Id, LeaderboardMetrics.CorrectAnswers,
            playEvent.StartsAtUtc, playEvent.EndsAtUtc, playEvent.EventKey);

        request.PrizeTiers = [PlayTest.CoinTier(1, 1, coins.Key, 5)];

        var updated = await PlayTest.EventAdmin(context).UpdateAsync(playEvent.EventId, request);

        // Entrants competed for the prize they were shown. Changing it on the last day would be
        // changing the deal after the fact.
        Assert.False(updated.Succeeded);
        Assert.Equal(ApiErrors.PlayEventInvalid.Code, updated.Error?.Code);
    }

    [Fact]
    public async Task Duplicating_an_event_schedules_the_next_one_inactive()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();
        var coins = await context.CreateCurrencyAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var request = PlayTest.EventRequest(
            game.Id, mode.Id, LeaderboardMetrics.CorrectAnswers,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow,
            eventKey: "runner.weekly.2026w37");

        request.PrizeTiers = [PlayTest.CoinTier(1, 3, coins.Key, 200)];

        var first = await PlayTest.EventAdmin(context).CreateAsync(request, Guid.NewGuid());
        Assert.True(first.Succeeded, string.Join("; ", first.Errors));

        var copy = await PlayTest.EventAdmin(context).DuplicateAsync(first.Value!.EventId, Guid.NewGuid());

        Assert.True(copy.Succeeded, string.Join("; ", copy.Errors));
        Assert.Equal("runner.weekly.2026w38", copy.Value!.EventKey);

        // Starts where the last one ended, and stays unpublished until somebody has read it through.
        Assert.Equal(first.Value.EndsAtUtc, copy.Value.StartsAtUtc);
        Assert.False(copy.Value.IsActive);
        Assert.Single(copy.Value.PrizeTiers);
    }

    [Fact]
    public async Task A_claim_moves_only_along_its_allowed_path_and_the_award_follows_it()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var operatorId = await TestData.CreateUserAsync(context);
        var game = await context.CreateGameAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true);

        var playEvent = await OpenEventAsync(
            context, game.Id, mode.Id, PlayTest.RealWorldTier(1, 1, "A tablet"));

        await context.AddResultAsync(
            userId, game.Id, LeaderboardMetrics.CorrectAnswers, 4, modeId: mode.Id, eventId: playEvent.EventId);

        await SettleAsync(context, playEvent);

        var claims = PlayTest.Claims(context);
        var queued = Assert.Single(await claims.ListAsync(eventId: playEvent.EventId));

        // Straight to delivered is refused: a prize is approved, then arranged, then delivered, and
        // skipping the middle step loses the record of who approved it.
        var skipped = await claims.UpdateAsync(
            queued.ClaimId, new UpdatePrizeClaimRequest { State = "fulfilled" }, operatorId);

        Assert.False(skipped.Succeeded);
        Assert.Equal(ApiErrors.PlayPrizeClaimInvalidTransition.Code, skipped.Error?.Code);

        var approved = await claims.UpdateAsync(
            queued.ClaimId,
            new UpdatePrizeClaimRequest { State = "awaiting_guardian", Note = "Guardian contacted" },
            operatorId);

        Assert.True(approved.Succeeded);

        var delivered = await claims.UpdateAsync(
            queued.ClaimId, new UpdatePrizeClaimRequest { State = "fulfilled" }, operatorId);

        Assert.True(delivered.Succeeded);

        await using var check = _fixture.CreateContext();
        var award = await check.EventAwards.FirstAsync(a => a.EventId == playEvent.EventId);

        // The winner's own screen reads the award, so it has to say what the operator's queue says.
        Assert.Equal(EventAwardState.Fulfilled, award.State);
    }

    private static async Task<long> BalanceAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context, Guid userId, Guid currencyId) =>
        await context.UserCurrencyBalances
            .Where(b => b.UserId == userId && b.CurrencyId == currencyId)
            .Select(b => b.Amount)
            .FirstOrDefaultAsync();
}
