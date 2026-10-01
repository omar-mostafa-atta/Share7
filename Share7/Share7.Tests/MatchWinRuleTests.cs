using Share7.Application.Multiplayer.Models;
using Share7.Application.Play.Models;
using Share7.Domain.Constants;
using Share7.Domain.Multiplayer;
using Share7.Domain.Play;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Win rules as an operator authors them: which metrics a mode may be decided on, in which order,
/// and how far each one can be trusted.
/// </summary>
public class MatchWinRuleTests
{
    [Theory]
    [InlineData("correct_answers", "correct_answers")]
    [InlineData("  Accuracy ", "accuracy")]
    [InlineData("signal:Kill", "signal:kill")]
    [InlineData("signal:mg147_starfish", "signal:mg147_starfish")]
    public void Metrics_are_normalised_to_their_stored_form(string typed, string stored) =>
        Assert.Equal(stored, MatchMetrics.Normalise(typed));

    [Theory]
    [InlineData("score")]
    [InlineData("signal:")]
    [InlineData("signal:Not A Kind")]
    // Answers are graded by the server; a run may not report them, so neither may a rule read them from one.
    [InlineData("signal:correct_answer")]
    public void Anything_a_match_cannot_be_decided_on_is_refused(string typed) =>
        Assert.Null(MatchMetrics.Normalise(typed));

    [Fact]
    public void Each_metric_says_how_far_the_server_can_vouch_for_it()
    {
        Assert.Equal(MatchMetricTrust.Verified, MatchMetrics.TrustOf(MatchMetrics.CorrectAnswers));
        Assert.Equal(MatchMetricTrust.Verified, MatchMetrics.TrustOf(MatchMetrics.Accuracy));
        Assert.Equal(MatchMetricTrust.Bounded, MatchMetrics.TrustOf(MatchMetrics.DurationMs));
        Assert.Equal(MatchMetricTrust.Bounded, MatchMetrics.TrustOf("signal:kill"));
        Assert.Equal(MatchMetricTrust.Reported, MatchMetrics.TrustOf(MatchMetrics.Outcome));
    }

    [Fact]
    public void A_rule_is_only_as_trustworthy_as_its_weakest_criterion()
    {
        var verified = MatchWinRule.Build([("correct_answers", "higher"), ("accuracy", "higher")]).Rule!;
        var mixed = MatchWinRule.Build([("correct_answers", "higher"), ("outcome", "higher")]).Rule!;

        Assert.Equal(MatchMetricTrust.Verified, verified.Trust);
        Assert.Equal(MatchMetricTrust.Reported, mixed.Trust);
    }

    [Fact]
    public void Every_problem_with_a_rule_is_reported_at_once()
    {
        var (rule, problems) = MatchWinRule.Build([
            ("score", "higher"),
            ("signal:kill", "sideways"),
            ("duration_ms", "lower"),
            ("duration_ms", "higher")
        ]);

        Assert.Null(rule);
        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void A_rule_has_at_most_five_criteria()
    {
        var (rule, problems) = MatchWinRule.Build(
            Enumerable.Range(0, 6).Select(i => ((string?)$"signal:k{i}", (string?)"higher")));

        Assert.Null(rule);
        Assert.Single(problems);
    }

    [Fact]
    public void A_rule_survives_storage_and_an_unreadable_one_decides_nothing()
    {
        var rule = MatchWinRule.Build([("outcome", "higher"), ("duration_ms", "higher"), ("signal:kill", "higher")]).Rule!;
        var back = MatchWinRule.Parse(rule.ToJson())!;

        Assert.Equal(rule.Criteria, back.Criteria);
        Assert.Null(MatchWinRule.Parse("{ not json"));
        Assert.Null(MatchWinRule.Parse("[]"));
    }
}

/// <summary>Win rules through the real mode-authoring service, against a real database.</summary>
[Collection(SqlServerCollection.Name)]
public class MatchWinRuleAuthoringTests
{
    private readonly SqlServerFixture _fixture;

    public MatchWinRuleAuthoringTests(SqlServerFixture fixture) => _fixture = fixture;

    private static SaveGameModeRequest Mode(Guid gameId, string key, List<MatchWinCriterionDto>? winRule, params string[] topologies) => new()
    {
        GameId = gameId,
        ModeKey = key,
        Topologies = topologies.Length == 0 ? ["solo", "versus"] : [.. topologies],
        MinPlayers = 1,
        MaxPlayers = 4,
        Translations =
        [
            new GameModeTranslationRequest { LangId = LanguageIds.English, Name = "Arena" },
            new GameModeTranslationRequest { LangId = LanguageIds.Arabic, Name = "الساحة" }
        ],
        WinRule = winRule
    };

    private static List<MatchWinCriterionDto> Rule(params (string Metric, string Order)[] criteria) =>
        [.. criteria.Select(c => new MatchWinCriterionDto { Metric = c.Metric, Order = c.Order })];

    [Fact]
    public async Task A_new_mode_chooses_how_its_matches_are_won()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();

        var created = await PlayTest.ModeAdmin(context).CreateAsync(Mode(game.Id, $"arena_{Guid.NewGuid():N}"[..20],
            Rule(("Outcome", "higher"), ("duration_ms", "higher"), ("signal:Kill", "higher"))));

        Assert.True(created.Succeeded, string.Join("; ", created.Errors));

        var rule = created.Value!.WinRule;
        Assert.Equal(["outcome", "duration_ms", "signal:kill"], rule.Select(c => c.Metric));
        Assert.All(rule, c => Assert.Equal("higher", c.Order));

        // Survived is the client's word — the console has to be able to say so before anyone puts a prize on it.
        Assert.Equal("reported", created.Value.WinRuleTrust);
    }

    [Fact]
    public async Task A_save_that_does_not_mention_the_rule_leaves_it_alone()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();
        var admin = PlayTest.ModeAdmin(context);
        var key = $"arena_{Guid.NewGuid():N}"[..20];

        var created = await admin.CreateAsync(Mode(game.Id, key, Rule(("correct_answers", "higher"))));

        // A console built before win rules sends every other field and not this one. That save must
        // not quietly delete a rule it did not know was there.
        var saved = await admin.UpdateAsync(created.Value!.ModeId, Mode(game.Id, key, winRule: null));

        Assert.True(saved.Succeeded, string.Join("; ", saved.Errors));
        Assert.Equal("correct_answers", Assert.Single(saved.Value!.WinRule).Metric);

        var cleared = await admin.UpdateAsync(created.Value.ModeId, Mode(game.Id, key, winRule: []));

        Assert.Empty(cleared.Value!.WinRule);
        Assert.Null(cleared.Value.WinRuleTrust);
    }

    [Fact]
    public async Task A_mode_nobody_plays_against_anybody_cannot_have_a_win_rule()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();

        var refused = await PlayTest.ModeAdmin(context).CreateAsync(
            Mode(game.Id, $"solo_{Guid.NewGuid():N}"[..20], Rule(("correct_answers", "higher")), "solo"));

        Assert.False(refused.Succeeded);
        Assert.Equal("PC_MODE_INVALID", refused.Error!.Code);
    }

    [Fact]
    public async Task A_rule_naming_nothing_a_match_can_be_decided_on_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();

        var refused = await PlayTest.ModeAdmin(context).CreateAsync(
            Mode(game.Id, $"arena_{Guid.NewGuid():N}"[..20], Rule(("score", "higher"), ("signal:correct_answer", "higher"))));

        Assert.False(refused.Succeeded);
        var problems = Assert.IsAssignableFrom<IEnumerable<string>>(refused.Details!["problems"]);
        Assert.Equal(2, problems.Count());
    }

    [Fact]
    public async Task The_client_sees_how_a_mode_is_won()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();

        var mode = await context.AddModeAsync(game.Id, isDefault: true, topologies: PlayTopologies.Solo | PlayTopologies.Versus);
        mode.WinRuleJson = MatchWinRule.Build([("correct_answers", "higher"), ("duration_ms", "lower")]).Rule!.ToJson();
        await context.SaveChangesAsync();

        var read = await PlayTest.Modes(context).GetForGameAsync(game.GameKey);

        var rule = Assert.Single(read!.Modes).WinRule;
        Assert.Equal(["correct_answers", "duration_ms"], rule.Select(c => c.Metric));
        Assert.Equal(["higher", "lower"], rule.Select(c => c.Order));
    }
    [Fact]
    public async Task Only_a_versus_mode_with_a_win_rule_can_be_ranked_and_it_stays_that_way()
    {
        await using var context = _fixture.CreateContext();
        var game = await context.CreateGameAsync();
        var admin = PlayTest.ModeAdmin(context);
        var key = $"ranked_{Guid.NewGuid():N}"[..20];

        var noRule = Mode(game.Id, key, winRule: null);
        noRule.Ranked = true;
        Assert.Equal("PC_MODE_INVALID", (await admin.CreateAsync(noRule)).Error?.Code);

        var ranked = Mode(game.Id, key, Rule(("correct_answers", "higher")));
        ranked.Ranked = true;
        var created = await admin.CreateAsync(ranked);

        Assert.True(created.Succeeded, string.Join("; ", created.Errors));
        Assert.True(created.Value!.Ranked);

        // A save that would leave a ranked mode with nothing to rank on is refused, not obeyed.
        var clearing = Mode(game.Id, key, winRule: []);
        Assert.Equal("PC_MODE_INVALID", (await admin.UpdateAsync(created.Value.ModeId, clearing)).Error?.Code);

        // An older console that knows nothing of ranked saves without switching it off.
        var older = await admin.UpdateAsync(created.Value.ModeId, Mode(game.Id, key, winRule: null));
        Assert.True(older.Value!.Ranked);

        var read = await PlayTest.Modes(context).GetForGameAsync(game.GameKey);
        Assert.True(Assert.Single(read!.Modes).Ranked);
    }
}
