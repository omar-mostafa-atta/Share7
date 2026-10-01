using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Domain.Feed;
using Share7.Domain.Multiplayer;
using Share7.Domain.Play;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;
using MSOptions = Microsoft.Extensions.Options.Options;

namespace Share7.Tests;

/// <summary>The rating model and the visible tiers it projects to — pure arithmetic, checked against known values.</summary>
public class RatingModelTests
{
    [Fact]
    public void Two_new_players_move_by_the_published_amounts()
    {
        // The reference values for the Bradley–Terry full-pair model with OpenSkill's defaults.
        var rated = RatingModel.Rate([(RatingModel.Initial, 1), (RatingModel.Initial, 2)]);

        Assert.Equal(27.635, rated[0].Mu, 2);
        Assert.Equal(22.365, rated[1].Mu, 2);
        Assert.Equal(8.066, rated[0].Sigma, 2);
        Assert.Equal(rated[0].Sigma, rated[1].Sigma, 6);
    }

    [Fact]
    public void A_tie_between_equals_moves_nobody_but_makes_both_surer()
    {
        var rated = RatingModel.Rate([(RatingModel.Initial, 1), (RatingModel.Initial, 1)]);

        Assert.Equal(RatingModel.InitialMu, rated[0].Mu, 6);
        Assert.Equal(RatingModel.InitialMu, rated[1].Mu, 6);
        Assert.True(rated[0].Sigma < RatingModel.InitialSigma);
    }

    [Fact]
    public void An_upset_moves_further_than_an_expected_win()
    {
        var strong = new Rating(32, 3);
        var weak = new Rating(18, 3);

        var expected = RatingModel.Rate([(strong, 1), (weak, 2)]);
        var upset = RatingModel.Rate([(strong, 2), (weak, 1)]);

        Assert.True(upset[1].Mu - weak.Mu > expected[0].Mu - strong.Mu);
    }

    [Fact]
    public void Four_players_with_a_shared_first_place_are_rated_in_one_update()
    {
        var rated = RatingModel.Rate([
            (RatingModel.Initial, 1), (RatingModel.Initial, 1), (RatingModel.Initial, 3), (RatingModel.Initial, 4)]);

        Assert.Equal(rated[0].Mu, rated[1].Mu, 6);
        Assert.True(rated[1].Mu > rated[2].Mu);
        Assert.True(rated[2].Mu > rated[3].Mu);
        Assert.True(rated[3].Mu < RatingModel.InitialMu);
    }

    [Theory]
    [InlineData(-4.0, "bronze", 3)]
    [InlineData(0.0, "bronze", 3)]
    [InlineData(7.9, "bronze", 1)]
    [InlineData(8.0, "silver", 3)]
    [InlineData(20.0, "gold", 2)]
    [InlineData(31.9, "platinum", 1)]
    [InlineData(32.0, "diamond", null)]
    [InlineData(55.0, "diamond", null)]
    public void Tiers_read_from_the_conservative_estimate(double ordinal, string tier, int? division) =>
        Assert.Equal((tier, division), RankedTiers.For(ordinal));

    [Fact]
    public void The_ladder_only_climbs_as_the_estimate_rises()
    {
        var steps = Enumerable.Range(0, 400).Select(i => RankedTiers.Step(i / 10.0)).ToList();

        Assert.True(steps.Zip(steps.Skip(1)).All(pair => pair.Second >= pair.First));
        Assert.True(steps[^1] > steps[0]);
    }

    [Fact]
    public void A_new_player_starts_at_the_bottom_and_seasons_are_calendar_months()
    {
        Assert.Equal(0, RatingModel.Initial.Ordinal, 6);
        Assert.Equal("2026-10", RankedSeasons.KeyFor(new DateTime(2026, 10, 31, 23, 59, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), RankedSeasons.EndOf("2026-10"));
    }
}

/// <summary>Applying matches to ratings and seasonal standings, against a real database.</summary>
[Collection(SqlServerCollection.Name)]
public class RatingServiceTests
{
    private readonly SqlServerFixture _fixture;

    public RatingServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    private static RatingService Ratings(ApplicationDbContext context, Action<Application.Multiplayer.Models.MultiplayerOptions>? configure = null)
    {
        var options = MultiplayerTest.Options(configure);
        return new RatingService(context, SocialTest.Publisher(context, options), MSOptions.Create(options), NullLogger<RatingService>.Instance);
    }

    private static async Task<Guid> RankedModeAsync(ApplicationDbContext context)
    {
        var game = await context.CreateGameAsync();
        var mode = await context.AddModeAsync(game.Id, isDefault: true, topologies: PlayTopologies.Solo | PlayTopologies.Versus);
        return mode.Id;
    }

    private static Task ApplyAsync(RatingService ratings, Guid modeId, Guid winner, Guid loser) =>
        ratings.ApplyAsync(Guid.NewGuid(), modeId, [new RatedPlacement(winner, 1, true), new RatedPlacement(loser, 2, false)]);

    [Fact]
    public async Task A_match_moves_both_ratings_once_and_records_why()
    {
        await using var context = _fixture.CreateContext();
        var modeId = await RankedModeAsync(context);
        var winner = await TestData.CreateUserAsync(context);
        var loser = await TestData.CreateUserAsync(context);
        var ratings = Ratings(context);

        var sessionId = Guid.NewGuid();
        RatedPlacement[] placements = [new(winner, 1, true), new(loser, 2, false)];

        await ratings.ApplyAsync(sessionId, modeId, placements);
        await ratings.ApplyAsync(sessionId, modeId, placements);

        var now = await ratings.RatingsAsync([winner, loser], modeId);
        Assert.True(now[winner].Mu > RatingModel.InitialMu);
        Assert.True(now[loser].Mu < RatingModel.InitialMu);

        // Rated once, however many times it is applied.
        Assert.Equal(2, await context.PlayerRatingChanges.CountAsync(c => c.SessionId == sessionId));
        Assert.Equal(1, (await context.PlayerRatings.SingleAsync(r => r.UserId == winner && r.ModeId == modeId)).MatchesPlayed);
    }

    [Fact]
    public async Task No_tier_is_shown_until_placements_are_played_and_then_it_only_climbs()
    {
        await using var context = _fixture.CreateContext();
        var modeId = await RankedModeAsync(context);
        var player = await TestData.CreateUserAsync(context);
        var ratings = Ratings(context, o => o.RankedPlacementMatches = 3);

        for (var i = 0; i < 2; i++)
            await ApplyAsync(ratings, modeId, player, await TestData.CreateUserAsync(context));

        var placing = (await ratings.StandingAsync(player, modeId)).Value!;
        Assert.True(placing.IsPlacement);
        Assert.Equal(1, placing.PlacementMatchesLeft);
        Assert.Null(placing.Tier);

        await ApplyAsync(ratings, modeId, player, await TestData.CreateUserAsync(context));
        var placed = (await ratings.StandingAsync(player, modeId)).Value!;
        Assert.False(placed.IsPlacement);
        Assert.NotNull(placed.Tier);
        Assert.Equal(3, placed.Wins);

        // A losing streak: the hidden rating falls, the visible tier does not.
        var peakMu = (await ratings.RatingsAsync([player], modeId))[player].Mu;

        for (var i = 0; i < 4; i++)
            await ApplyAsync(ratings, modeId, await TestData.CreateUserAsync(context), player);

        var after = (await ratings.StandingAsync(player, modeId)).Value!;
        Assert.Equal((placed.Tier, placed.Division), (after.Tier, after.Division));
        Assert.True((await ratings.RatingsAsync([player], modeId))[player].Mu < peakMu);

        Assert.NotEmpty(await SocialTest.EventsAsync(context, player, PlayerEventTypes.RankedUpdated));
    }

    [Fact]
    public async Task The_same_two_players_stop_moving_each_other_after_the_daily_limit()
    {
        await using var context = _fixture.CreateContext();
        var modeId = await RankedModeAsync(context);
        var a = await TestData.CreateUserAsync(context);
        var b = await TestData.CreateUserAsync(context);
        var ratings = Ratings(context, o => o.RankedRepeatOpponentLimitPerDay = 2);

        await ApplyAsync(ratings, modeId, a, b);
        await ApplyAsync(ratings, modeId, a, b);
        var before = await ratings.RatingsAsync([a, b], modeId);

        var third = Guid.NewGuid();
        await ratings.ApplyAsync(third, modeId, [new RatedPlacement(a, 1, true), new RatedPlacement(b, 2, false)]);

        var after = await ratings.RatingsAsync([a, b], modeId);
        Assert.Equal(before[a], after[a]);
        Assert.Equal(before[b], after[b]);
        Assert.All(await context.PlayerRatingChanges.Where(c => c.SessionId == third).ToListAsync(),
            c => Assert.Equal("repeat_opponent", c.SkippedReason));
        Assert.Equal("repeat_opponent", (await ratings.ResultForAsync(third, a))!.NotCountedReason);
    }

    [Fact]
    public async Task A_player_who_never_played_ranked_reads_as_new()
    {
        await using var context = _fixture.CreateContext();
        var modeId = await RankedModeAsync(context);
        var stranger = await TestData.CreateUserAsync(context);

        Assert.Equal(RatingModel.Initial, (await Ratings(context).RatingsAsync([stranger], modeId))[stranger]);

        var standing = (await Ratings(context).StandingAsync(stranger, modeId)).Value!;
        Assert.True(standing.IsPlacement);
        Assert.Equal(0, standing.MatchesPlayed);
    }
}
