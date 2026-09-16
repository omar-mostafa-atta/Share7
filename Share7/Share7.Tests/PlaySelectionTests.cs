using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Play.Models;
using Share7.Domain.Play;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The gate every session passes through: which mode, why it is being played, and what the pair is
/// allowed to be worth.
/// <para>
/// **Every refusal here is one a child would otherwise hit as a broken game rather than a message.**
/// A mode that no longer exists, one that was withdrawn this morning, one they have not bought — all
/// of them have to be a named code the client can render, because the alternative is a session that
/// opens and then behaves wrongly.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PlaySelectionTests
{
    private readonly SqlServerFixture _fixture;

    public PlaySelectionTests(SqlServerFixture fixture) => _fixture = fixture;

    private static PlaySelectionRequest Request(
        Guid gameId, string? modeKey = null, string? contextKey = null, Guid? eventId = null, int players = 1) =>
        new()
        {
            GameId = gameId,
            ModeKey = modeKey,
            ContextKey = contextKey,
            EventId = eventId,
            PlayerCount = players
        };

    [Fact]
    public async Task No_mode_key_resolves_to_the_games_default()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await context.AddModeAsync(path.GameId);
        var expected = await context.AddModeAsync(path.GameId, isDefault: true);

        var result = await PlayTest.Resolver(context).ResolveAsync(userId, Request(path.GameId));

        Assert.True(result.Succeeded);
        Assert.Equal(expected.Id, result.Value!.ModeId);
    }

    [Fact]
    public async Task A_game_with_no_modes_still_plays()
    {
        // The compatibility path, and the reason this whole domain could ship without a flag day: a
        // database migrated but not yet seeded must behave exactly as it did before modes existed.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var result = await PlayTest.Resolver(context).ResolveAsync(userId, Request(path.GameId));

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.ModeId);
        Assert.True(result.Value.Policy.AffectsMastery);
        Assert.True(result.Value.Policy.SettlesEconomy);
    }

    [Fact]
    public async Task An_unknown_mode_key_is_refused_rather_than_defaulted()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await context.AddModeAsync(path.GameId, isDefault: true);

        var result = await PlayTest.Resolver(context)
            .ResolveAsync(userId, Request(path.GameId, "runner.typo"));

        // Silently defaulting is how every run of a mis-spelled mode ends up priced as Classic and
        // nobody ever finds out.
        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.PlayModeUnknown.Code, result.Error?.Code);
    }

    [Fact]
    public async Task A_withdrawn_mode_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var mode = await context.AddModeAsync(path.GameId, isActive: false);

        var result = await PlayTest.Resolver(context).ResolveAsync(userId, Request(path.GameId, mode.ModeKey));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.PlayModeInactive.Code, result.Error?.Code);
    }

    [Fact]
    public async Task A_mode_outside_its_window_is_refused_with_the_window_in_the_details()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var mode = await context.AddModeAsync(
            path.GameId, "runner.friday",
            availableFromUtc: DateTime.UtcNow.AddDays(2),
            availableToUtc: DateTime.UtcNow.AddDays(3));

        var result = await PlayTest.Resolver(context).ResolveAsync(userId, Request(path.GameId, mode.ModeKey));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.PlayModeInactive.Code, result.Error?.Code);

        // The client says "opens Friday" from these, so their absence is a blank refusal.
        Assert.NotNull(result.Details?["availableFromUtc"]);
    }

    [Fact]
    public async Task A_mode_belonging_to_another_game_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var mine = await TestData.CreateCurriculumPathAsync(context);
        var theirs = await TestData.CreateCurriculumPathAsync(context);

        var mode = await context.AddModeAsync(theirs.GameId, "other.mode");

        var result = await PlayTest.Resolver(context).ResolveAsync(userId, Request(mine.GameId, mode.ModeKey));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.PlayModeWrongGame.Code, result.Error?.Code);
    }

    [Fact]
    public async Task A_solo_only_mode_refuses_a_multiplayer_seat_count()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var mode = await context.AddModeAsync(path.GameId, topologies: PlayTopologies.Solo);

        var result = await PlayTest.Resolver(context)
            .ResolveAsync(userId, Request(path.GameId, mode.ModeKey, players: 2));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.PlayTopologyMismatch.Code, result.Error?.Code);
    }

    [Fact]
    public async Task A_sold_mode_is_refused_until_it_is_owned_and_then_allowed()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var product = await context.CreateProductAsync();

        var mode = await context.AddModeAsync(path.GameId, entitlementProductId: product.Id);

        var refused = await PlayTest.Resolver(context).ResolveAsync(userId, Request(path.GameId, mode.ModeKey));

        Assert.False(refused.Succeeded);
        Assert.Equal(ApiErrors.PlayModeNotEntitled.Code, refused.Error?.Code);

        await new Share7.Infrastructure.Commerce.EntitlementService(context).GrantAsync(
            userId, product.Id, Share7.Domain.Commerce.EntitlementSource.AdminGrant, "test");

        var allowed = await PlayTest.Resolver(context).ResolveAsync(userId, Request(path.GameId, mode.ModeKey));

        Assert.True(allowed.Succeeded);
    }

    [Fact]
    public async Task Practice_is_worth_nothing_whatever_the_mode_says()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        // A mode that counts for everything. The context still wins.
        var mode = await context.AddModeAsync(path.GameId, isDefault: true);

        var result = await PlayTest.Resolver(context)
            .ResolveAsync(userId, Request(path.GameId, mode.ModeKey, PlayContextTokens.Practice));

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.Policy.AffectsMastery);
        Assert.False(result.Value.Policy.SettlesEconomy);
        Assert.False(result.Value.Policy.Ranks);
    }

    [Fact]
    public async Task A_mode_that_never_counts_cannot_be_made_to_by_playing_the_curriculum()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var mode = await context.AddModeAsync(path.GameId, countsTowardMastery: false);

        var result = await PlayTest.Resolver(context)
            .ResolveAsync(userId, Request(path.GameId, mode.ModeKey, PlayContextTokens.Curriculum));

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.Policy.AffectsMastery);

        // It still pays and still ranks — "does not move mastery" is not "is worth nothing".
        Assert.True(result.Value.Policy.SettlesEconomy);
        Assert.True(result.Value.Policy.Ranks);
    }

    [Fact]
    public async Task Free_play_pays_and_ranks_but_never_moves_mastery()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var mode = await context.AddModeAsync(path.GameId, isDefault: true);

        var result = await PlayTest.Resolver(context)
            .ResolveAsync(userId, Request(path.GameId, mode.ModeKey, PlayContextTokens.FreePlay));

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.Policy.AffectsMastery);
        Assert.True(result.Value.Policy.SettlesEconomy);
    }

    [Fact]
    public async Task An_event_context_with_no_event_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await context.AddModeAsync(path.GameId, isDefault: true);

        var result = await PlayTest.Resolver(context)
            .ResolveAsync(userId, Request(path.GameId, contextKey: PlayContextTokens.Event));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.PlayContextInvalid.Code, result.Error?.Code);
    }

    [Fact]
    public async Task An_event_id_sent_without_an_event_context_is_refused_rather_than_ignored()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await context.AddModeAsync(path.GameId, isDefault: true);

        var result = await PlayTest.Resolver(context)
            .ResolveAsync(userId, Request(path.GameId, contextKey: PlayContextTokens.Curriculum, eventId: Guid.NewGuid()));

        // Ignoring it would mean a child playing an event whose entry was never counted, and being
        // told nothing about it.
        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.PlayContextInvalid.Code, result.Error?.Code);
    }

    [Fact]
    public async Task Assignments_are_refused_rather_than_treated_as_curriculum()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        await context.AddModeAsync(path.GameId, isDefault: true);

        var result = await PlayTest.Resolver(context)
            .ResolveAsync(userId, Request(path.GameId, contextKey: PlayContextTokens.Assignment));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.PlayContextInvalid.Code, result.Error?.Code);
    }

    [Fact]
    public async Task The_economy_profile_comes_from_the_mode_and_scales_the_payout()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var half = await context.AddEconomyProfileAsync(50);
        var mode = await context.AddModeAsync(path.GameId, economyProfileId: half.Id);

        var result = await PlayTest.Resolver(context).ResolveAsync(userId, Request(path.GameId, mode.ModeKey));

        Assert.True(result.Succeeded);
        Assert.Equal(50, result.Value!.Profile?.PayoutPercent);
        Assert.Equal(50, result.Value.Scale(100));

        // Rounded down, because rounding up mints currency nobody authored — and at one coin a
        // farming loop repeats that all day.
        Assert.Equal(0, result.Value.Scale(1));
    }

    [Fact]
    public async Task A_mode_with_no_profile_settles_under_the_platform_default()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);

        var standard = await context.AddEconomyProfileAsync(100, isDefault: true);
        var mode = await context.AddModeAsync(path.GameId, isDefault: true);

        var result = await PlayTest.Resolver(context).ResolveAsync(userId, Request(path.GameId, mode.ModeKey));

        Assert.True(result.Succeeded);
        Assert.Equal(standard.Id, result.Value!.Profile?.Id);
        Assert.Equal(100, result.Value.Scale(100));

        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM [EconomyProfiles] WHERE [Id] = {0}", standard.Id);
    }
}
