using Microsoft.EntityFrameworkCore;
using Share7.Domain.Economy;
using Share7.Domain.Progress;
using Share7.Domain.Rewards;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The reward engine against a real database.
/// <para>
/// Every rule here is scoped to its own test's lesson via <c>referenceKey</c>. Rules are global
/// configuration and the collection shares one database, so a rule left matching every lesson
/// would quietly pay out inside unrelated tests and make them pass for the wrong reason.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RewardServiceTests
{
    private readonly SqlServerFixture _fixture;

    public RewardServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task One_rule_pays_every_currency_it_grants()
    {
        // The multi-currency case: one event, one rule, one cooldown, two currencies.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();
        var gems = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(coins.Id, 10), new GrantSpec(gems.Id, 2)],
            referenceKey: path.LessonId.ToString());

        var rewards = await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Completed));

        var reward = Assert.Single(rewards);
        Assert.Equal(2, reward.Grants.Count);
        Assert.Equal(10, reward.Grants.Single(g => g.Currency == coins.Key).Amount);
        Assert.Equal(2, reward.Grants.Single(g => g.Currency == gems.Key).Amount);

        await using var check = _fixture.CreateContext();
        Assert.Equal(10, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Equal(2, await check.BalanceOfAsync(userId, gems.Id));

        // One transaction covering both currencies, not two transactions of one.
        var transaction = Assert.Single(await check.RewardTransactionsOfAsync(userId));
        Assert.Equal(2, transaction.Lines.Count);
    }

    [Fact]
    public async Task Reward_lines_point_at_the_ledger_entries_that_moved_the_balance()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();
        var gems = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 7), new GrantSpec(gems.Id, 3)],
            referenceKey: path.LessonId.ToString());

        await context.EvaluateRewardsAsync(RewardTestExtensions.Attempt(userId, path));

        await using var check = _fixture.CreateContext();
        var transaction = Assert.Single(await check.RewardTransactionsOfAsync(userId));
        var ledger = await check.LedgerOfAsync(userId);

        Assert.Equal(2, ledger.Count);

        foreach (var line in transaction.Lines)
        {
            var entry = ledger.Single(e => e.Id == line.LedgerEntryId);
            Assert.Equal(line.Amount, entry.Amount);
            Assert.Equal(line.BalanceAfter, entry.BalanceAfter);
            Assert.Equal(transaction.IdempotencyKey, entry.IdempotencyKey);

            // Stamped from the rule, so a later kind of reward is configuration rather than a
            // migration and a switch statement.
            Assert.Equal(CurrencyTransactionType.LessonReward, entry.TransactionType);
        }
    }

    [Fact]
    public async Task A_once_rule_pays_only_the_first_time()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(coins.Id, 25)],
            referenceKey: path.LessonId.ToString());

        var first = await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Completed, attemptNumber: 1));

        var second = await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Completed, attemptNumber: 2));

        Assert.Single(first);

        // Not a replay — a genuinely later attempt that earned nothing. Reporting the original
        // reward here would have the client animate coins it is not receiving.
        Assert.Empty(second);

        await using var check = _fixture.CreateContext();
        Assert.Equal(25, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Single(await check.RewardTransactionsOfAsync(userId));
    }

    [Fact]
    public async Task Retrying_one_submission_replays_the_original_reward_without_paying_again()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(coins.Id, 40)],
            referenceKey: path.LessonId.ToString());

        var attempt = RewardTestExtensions.Attempt(
            userId, path, CompletionState.Completed, requestId: "retry-me");

        var first = await context.EvaluateRewardsAsync(attempt);
        var retry = await context.EvaluateRewardsAsync(attempt);

        // Same outcome, same transaction id, charged once — the purchase contract's retry rule,
        // applied to earning.
        Assert.Equal(Assert.Single(first).TransactionId, Assert.Single(retry).TransactionId);
        Assert.Equal(40, Assert.Single(Assert.Single(retry).Grants).Amount);

        await using var check = _fixture.CreateContext();
        Assert.Equal(40, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Single(await check.LedgerOfAsync(userId));
    }

    [Fact]
    public async Task An_every_time_rule_pays_on_each_distinct_attempt()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAttempted,
            [new GrantSpec(coins.Id, 3)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: path.LessonId.ToString());

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var rewards = await context.EvaluateRewardsAsync(
                RewardTestExtensions.Attempt(userId, path, CompletionState.Uncompleted, attempt));

            Assert.Single(rewards);
        }

        await using var check = _fixture.CreateContext();
        Assert.Equal(12, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Equal(4, (await check.RewardTransactionsOfAsync(userId)).Count);
    }

    [Fact]
    public async Task A_cooldown_suppresses_the_next_payout()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAttempted,
            [new GrantSpec(coins.Id, 5)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: path.LessonId.ToString(),
            cooldownSeconds: 3600);

        var first = await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Uncompleted, 1));

        var second = await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Uncompleted, 2));

        Assert.Single(first);
        Assert.Empty(second);

        await using var check = _fixture.CreateContext();
        Assert.Equal(5, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_daily_limit_caps_how_often_a_rule_pays()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAttempted,
            [new GrantSpec(coins.Id, 5)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: path.LessonId.ToString(),
            dailyLimit: 2);

        var paid = 0;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var rewards = await context.EvaluateRewardsAsync(
                RewardTestExtensions.Attempt(userId, path, CompletionState.Uncompleted, attempt));

            paid += rewards.Count;
        }

        Assert.Equal(2, paid);

        await using var check = _fixture.CreateContext();
        Assert.Equal(10, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_retired_currency_anywhere_in_a_rule_stops_the_whole_payout()
    {
        // The multi-currency atomicity case. Paying the coins and skipping the gems would record
        // the reward as complete and the student would never receive the rest.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();
        var retired = await context.CreateCurrencyAsync(enabled: false);

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 10), new GrantSpec(retired.Id, 5)],
            referenceKey: path.LessonId.ToString());

        var rewards = await context.EvaluateRewardsAsync(RewardTestExtensions.Attempt(userId, path));

        Assert.Empty(rewards);

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Equal(0, await check.BalanceOfAsync(userId, retired.Id));
        Assert.Empty(await check.LedgerOfAsync(userId));

        // And nothing claimed the key, so fixing the currency later lets the reward pay properly.
        Assert.Empty(await check.RewardTransactionsOfAsync(userId));
    }

    [Fact]
    public async Task An_unpayable_rule_does_not_stop_the_others()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();
        var retired = await context.CreateCurrencyAsync(enabled: false);

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(retired.Id, 5)],
            referenceKey: path.LessonId.ToString());

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(coins.Id, 15)],
            referenceKey: path.LessonId.ToString());

        var rewards = await context.EvaluateRewardsAsync(RewardTestExtensions.Attempt(userId, path));

        Assert.Single(rewards);

        await using var check = _fixture.CreateContext();
        Assert.Equal(15, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task Concurrent_duplicate_submissions_pay_exactly_once()
    {
        // The unique index doing the work the preceding SELECT cannot: every one of these reads
        // "not yet paid" before any of them inserts.
        await using var setup = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(setup);
        var path = await TestData.CreateCurriculumPathAsync(setup);
        var coins = await setup.CreateCurrencyAsync();

        await setup.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted,
            [new GrantSpec(coins.Id, 50)],
            RewardRepeatPolicy.EveryTime,
            referenceKey: path.LessonId.ToString());

        var attempt = RewardTestExtensions.Attempt(
            userId, path, CompletionState.Completed, requestId: "one-submission");

        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await context.EvaluateRewardsAsync(attempt);
        });

        var results = await Task.WhenAll(tasks);

        // Callers legitimately split two ways, and both are correct: whoever reads before the
        // winner commits loses the insert and reports nothing, whoever reads after it finds the
        // committed row and replays it. What must never differ is the money.
        var reported = results.SelectMany(r => r).Select(r => r.TransactionId).Distinct().ToList();
        Assert.Single(reported);

        await using var check = _fixture.CreateContext();
        Assert.Equal(50, await check.BalanceOfAsync(userId, coins.Id));
        Assert.Equal(reported[0], Assert.Single(await check.RewardTransactionsOfAsync(userId)).Id);
        Assert.Single(await check.LedgerOfAsync(userId));
    }

    [Fact]
    public async Task An_aced_attempt_fires_the_attempted_completed_and_aced_rules()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();
        var reference = path.LessonId.ToString();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAttempted, [new GrantSpec(coins.Id, 1)], referenceKey: reference);
        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted, [new GrantSpec(coins.Id, 10)], referenceKey: reference);
        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced, [new GrantSpec(coins.Id, 100)], referenceKey: reference);

        var rewards = await context.EvaluateRewardsAsync(RewardTestExtensions.Attempt(userId, path));

        Assert.Equal(3, rewards.Count);

        await using var check = _fixture.CreateContext();
        Assert.Equal(111, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_failed_attempt_fires_only_the_attempted_rule()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();
        var reference = path.LessonId.ToString();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAttempted, [new GrantSpec(coins.Id, 1)], referenceKey: reference);
        await context.CreateRewardRuleAsync(
            RewardEventType.LessonCompleted, [new GrantSpec(coins.Id, 10)], referenceKey: reference);

        var rewards = await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Uncompleted));

        Assert.Single(rewards);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_retired_rule_does_not_pay()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 10)],
            referenceKey: path.LessonId.ToString(),
            enabled: false);

        Assert.Empty(await context.EvaluateRewardsAsync(RewardTestExtensions.Attempt(userId, path)));

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.BalanceOfAsync(userId, coins.Id));
    }

    [Fact]
    public async Task A_global_rule_and_a_lesson_rule_both_pay()
    {
        // Rules compose instead of overriding: "10 for any lesson, 50 more for this one" is two
        // rules and 60 coins, with no precedence to reason about.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        var global = await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced, [new GrantSpec(coins.Id, 10)], referenceKey: null);

        try
        {
            await context.CreateRewardRuleAsync(
                RewardEventType.LessonAced,
                [new GrantSpec(coins.Id, 50)],
                referenceKey: path.LessonId.ToString());

            var rewards = await context.EvaluateRewardsAsync(RewardTestExtensions.Attempt(userId, path));

            Assert.Equal(2, rewards.Count);

            await using var check = _fixture.CreateContext();
            Assert.Equal(60, await check.BalanceOfAsync(userId, coins.Id));
        }
        finally
        {
            // This is the one rule in the file that matches every lesson. Left enabled it would
            // pay out inside every later test in the collection.
            await using var cleanup = _fixture.CreateContext();
            await cleanup.RewardRules
                .Where(r => r.Id == global.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Enabled, false));
        }
    }

    [Fact]
    public async Task Rewards_for_the_same_lesson_are_tracked_separately_per_game()
    {
        // Progress is per game, so a lesson's rewards are too — starting a second game must not
        // find its first-completion reward already spent.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var otherGame = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();

        await context.CreateRewardRuleAsync(
            RewardEventType.LessonAced,
            [new GrantSpec(coins.Id, 20)],
            referenceKey: path.LessonId.ToString());

        await context.EvaluateRewardsAsync(RewardTestExtensions.Attempt(userId, path));

        var inSecondGame = await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path with { GameId = otherGame.GameId }));

        Assert.Single(inSecondGame);

        await using var check = _fixture.CreateContext();
        Assert.Equal(40, await check.BalanceOfAsync(userId, coins.Id));
    }
}
