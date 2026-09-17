using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Rewards.Models;
using Share7.Domain.Progress;
using Share7.Infrastructure.Rewards;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Rule authoring. The theme throughout: a rule that could never pay is refused at the point it is
/// written, because once stored it looks identical to a working one and the only symptom is
/// students quietly earning nothing.
/// <para>
/// Every rule here is scoped to a lesson-shaped <c>referenceKey</c> so it cannot match, and pay
/// out inside, another test in the collection.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RewardAdminServiceTests
{
    private readonly SqlServerFixture _fixture;

    public RewardAdminServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_rule_can_grant_several_currencies_at_once()
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();
        var gems = await context.CreateCurrencyAsync();

        var result = await new RewardAdminService(context).CreateRuleAsync(new CreateRewardRuleRequest
        {
            Name = "Lesson passed",
            EventType = "LESSON_COMPLETED",
            ReferenceKey = Guid.NewGuid().ToString(),
            RepeatPolicy = "ONCE",
            Grants =
            [
                new RewardGrantRequest { CurrencyId = coins.Id, Amount = 10 },
                new RewardGrantRequest { CurrencyId = gems.Id, Amount = 2 }
            ]
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        // Vocabulary round-trips in the form the contract specifies, not the C# member name.
        Assert.Equal("LESSON_COMPLETED", result.Value!.EventType);
        Assert.Equal("ONCE", result.Value.RepeatPolicy);
        Assert.Equal("LESSON_REWARD", result.Value.TransactionType);

        Assert.Equal(2, result.Value.Grants.Count);
        Assert.Equal(10, result.Value.Grants.Single(g => g.Currency == coins.Key).Amount);
        Assert.Equal(2, result.Value.Grants.Single(g => g.Currency == gems.Key).Amount);
    }

    [Theory]
    [InlineData("lesson_completed")]
    [InlineData("LessonCompleted")]
    public async Task Event_types_are_accepted_in_any_reasonable_spelling(string spelling)
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();

        var result = await new RewardAdminService(context).CreateRuleAsync(Rule(coins.Id, eventType: spelling));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal("LESSON_COMPLETED", result.Value!.EventType);
    }

    [Fact]
    public async Task An_unknown_event_type_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();

        var result = await new RewardAdminService(context)
            .CreateRuleAsync(Rule(coins.Id, eventType: "MULTIPLAYER_WIN"));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.RewardRuleInvalid.Code, result.Error!.Code);
        Assert.Equal("rewards.rule.invalid", result.Error.MessageKey);
    }

    [Fact]
    public async Task A_limit_the_repeat_policy_would_ignore_is_refused()
    {
        // Silently dropping it would leave an admin believing a cap is in force that is not.
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();

        var request = Rule(coins.Id);
        request.RepeatPolicy = "ONCE";
        request.DailyLimit = 3;

        var result = await new RewardAdminService(context).CreateRuleAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.RewardRuleInvalid.Code, result.Error!.Code);
    }

    [Fact]
    public async Task The_same_currency_twice_in_one_rule_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();

        var request = Rule(coins.Id);
        request.Grants.Add(new RewardGrantRequest { CurrencyId = coins.Id, Amount = 5 });

        var result = await new RewardAdminService(context).CreateRuleAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.RewardRuleInvalid.Code, result.Error!.Code);
    }

    [Fact]
    public async Task A_rule_granting_a_currency_that_does_not_exist_is_refused()
    {
        await using var context = _fixture.CreateContext();

        var result = await new RewardAdminService(context).CreateRuleAsync(Rule(Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.CurrencyNotFound.Code, result.Error!.Code);
    }

    [Fact]
    public async Task A_non_positive_amount_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();

        var result = await new RewardAdminService(context).CreateRuleAsync(Rule(coins.Id, amount: 0));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.RewardRuleInvalid.Code, result.Error!.Code);
    }

    [Fact]
    public async Task A_reference_key_that_is_not_a_lesson_id_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();

        var request = Rule(coins.Id);
        request.ReferenceKey = "chapter-3";

        var result = await new RewardAdminService(context).CreateRuleAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.RewardRuleInvalid.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Updating_replaces_the_whole_grant_set()
    {
        // Includes re-sending a currency the rule already had, which is what forces the delete to
        // reach the database ahead of the inserts.
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();
        var gems = await context.CreateCurrencyAsync();
        var service = new RewardAdminService(context);

        var created = await service.CreateRuleAsync(Rule(coins.Id, amount: 10));
        Assert.True(created.Succeeded, string.Join("; ", created.Errors));

        var updated = await service.UpdateRuleAsync(created.Value!.RuleId, new UpdateRewardRuleRequest
        {
            Name = "Lesson passed, revised",
            RepeatPolicy = "EVERY_TIME",
            CooldownSeconds = 600,
            Grants =
            [
                new RewardGrantRequest { CurrencyId = coins.Id, Amount = 25 },
                new RewardGrantRequest { CurrencyId = gems.Id, Amount = 1 }
            ]
        });

        Assert.True(updated.Succeeded, string.Join("; ", updated.Errors));
        Assert.Equal("EVERY_TIME", updated.Value!.RepeatPolicy);
        Assert.Equal(600, updated.Value.CooldownSeconds);
        Assert.Equal(25, updated.Value.Grants.Single(g => g.Currency == coins.Key).Amount);
        Assert.Equal(1, updated.Value.Grants.Single(g => g.Currency == gems.Key).Amount);

        await using var check = _fixture.CreateContext();
        Assert.Equal(2, await check.RewardRuleGrants.CountAsync(g => g.RewardRuleId == created.Value.RuleId));
    }

    [Fact]
    public async Task Updating_a_rule_loaded_by_a_fresh_context_replaces_its_grants()
    {
        // What actually happens in production: create and update are separate requests, so the
        // update's DbContext has never seen the rule. The same-context test above passes even when
        // this path is broken.
        await using var create = _fixture.CreateContext();
        var coins = await create.CreateCurrencyAsync();
        var gems = await create.CreateCurrencyAsync();

        var created = await new RewardAdminService(create).CreateRuleAsync(Rule(coins.Id, amount: 10));
        Assert.True(created.Succeeded, string.Join("; ", created.Errors));

        await using var update = _fixture.CreateContext();

        var result = await new RewardAdminService(update).UpdateRuleAsync(
            created.Value!.RuleId,
            new UpdateRewardRuleRequest
            {
                Name = "Revised",
                RepeatPolicy = "EVERY_TIME",
                CooldownSeconds = 600,
                Grants =
                [
                    new RewardGrantRequest { CurrencyId = coins.Id, Amount = 25 },
                    new RewardGrantRequest { CurrencyId = gems.Id, Amount = 1 }
                ]
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal("Revised", result.Value!.Name);
        Assert.Equal("EVERY_TIME", result.Value.RepeatPolicy);
        Assert.Equal(2, result.Value.Grants.Count);

        await using var check = _fixture.CreateContext();
        Assert.Equal(2, await check.RewardRuleGrants.CountAsync(g => g.RewardRuleId == created.Value.RuleId));
    }

    [Fact]
    public async Task Updating_a_rule_that_does_not_exist_reports_not_found()
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();

        var result = await new RewardAdminService(context).UpdateRuleAsync(
            Guid.NewGuid(),
            new UpdateRewardRuleRequest
            {
                Name = "Nothing",
                Grants = [new RewardGrantRequest { CurrencyId = coins.Id, Amount = 1 }]
            });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.RewardRuleNotFound.Code, result.Error!.Code);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task Retiring_a_rule_stops_it_paying_without_losing_its_history()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var path = await TestData.CreateCurriculumPathAsync(context);
        var coins = await context.CreateCurrencyAsync();
        var service = new RewardAdminService(context);

        var created = await service.CreateRuleAsync(new CreateRewardRuleRequest
        {
            Name = "Every run",
            EventType = "LESSON_ATTEMPTED",
            ReferenceKey = path.LessonId.ToString(),
            RepeatPolicy = "EVERY_TIME",
            Grants = [new RewardGrantRequest { CurrencyId = coins.Id, Amount = 8 }]
        });

        Assert.True(created.Succeeded, string.Join("; ", created.Errors));

        await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Uncompleted, 1));

        var retired = await service.UpdateRuleAsync(created.Value!.RuleId, new UpdateRewardRuleRequest
        {
            Name = created.Value.Name,
            RepeatPolicy = "EVERY_TIME",
            Grants = [new RewardGrantRequest { CurrencyId = coins.Id, Amount = 8 }],
            Enabled = false
        });

        Assert.True(retired.Succeeded, string.Join("; ", retired.Errors));

        await context.EvaluateRewardsAsync(
            RewardTestExtensions.Attempt(userId, path, CompletionState.Uncompleted, 2));

        await using var check = _fixture.CreateContext();
        Assert.Equal(8, await check.BalanceOfAsync(userId, coins.Id));

        // The payment it already made stays on the books — that is why rules retire instead of
        // being deleted.
        Assert.Single(await check.RewardTransactionsOfAsync(userId));
    }

    private static CreateRewardRuleRequest Rule(
        Guid currencyId,
        string eventType = "LESSON_COMPLETED",
        long amount = 10) => new()
    {
        Name = "Test rule",
        EventType = eventType,
        // Lesson-shaped and unique, so this rule cannot match any other test's attempt.
        ReferenceKey = Guid.NewGuid().ToString(),
        RepeatPolicy = "ONCE",
        Grants = [new RewardGrantRequest { CurrencyId = currencyId, Amount = amount }]
    };
}
