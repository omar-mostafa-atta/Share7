using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Models;
using Share7.Domain.Economy;
using Share7.Infrastructure.Economy;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class WalletServiceTests
{
    private readonly SqlServerFixture _fixture;

    public WalletServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Credit_creates_the_balance_and_a_matching_ledger_entry()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();

        var result = await new WalletService(context).ApplyAsync(Credit(userId, currency.Id, 50));

        Assert.True(result.Succeeded);
        Assert.Equal(50, result.Value!.Amount);
        Assert.Equal(currency.Key, result.Value.Currency);

        await using var check = _fixture.CreateContext();
        Assert.Equal(50, await check.BalanceOfAsync(userId, currency.Id));

        var ledger = await check.LedgerOfAsync(userId);
        var entry = Assert.Single(ledger);
        Assert.Equal(50, entry.Amount);
        Assert.Equal(50, entry.BalanceAfter);
        Assert.Equal(CurrencyTransactionType.LessonReward, entry.TransactionType);
    }

    [Fact]
    public async Task Balance_after_tracks_the_running_total()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();
        var wallet = new WalletService(context);

        await wallet.ApplyAsync(Credit(userId, currency.Id, 100));
        await wallet.ApplyAsync(Credit(userId, currency.Id, 25));
        await wallet.ApplyAsync(Debit(userId, currency.Id, -30));

        await using var check = _fixture.CreateContext();
        var ledger = await check.LedgerOfAsync(userId);

        Assert.Equal([100, 25, -30], ledger.Select(e => e.Amount));
        Assert.Equal([100, 125, 95], ledger.Select(e => e.BalanceAfter));
        Assert.Equal(95, await check.BalanceOfAsync(userId, currency.Id));
    }

    [Fact]
    public async Task Ledger_can_rebuild_the_balance()
    {
        // The property that makes the ledger the source of truth rather than a log.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();
        var wallet = new WalletService(context);

        foreach (var delta in new[] { 500L, -120L, 60L, -15L, 7L })
            await wallet.ApplyAsync(delta > 0 ? Credit(userId, currency.Id, delta) : Debit(userId, currency.Id, delta));

        await using var check = _fixture.CreateContext();
        var summed = (await check.LedgerOfAsync(userId)).Sum(e => e.Amount);

        Assert.Equal(await check.BalanceOfAsync(userId, currency.Id), summed);
    }

    [Fact]
    public async Task Overdrawing_is_refused_and_changes_nothing()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();
        var wallet = new WalletService(context);

        await wallet.ApplyAsync(Credit(userId, currency.Id, 40));
        var result = await wallet.ApplyAsync(Debit(userId, currency.Id, -41));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.InsufficientBalance.Code, result.Error!.Code);
        Assert.Equal("commerce.insufficient_balance", result.Error.MessageKey);

        await using var check = _fixture.CreateContext();
        Assert.Equal(40, await check.BalanceOfAsync(userId, currency.Id));

        // A refusal must not leave a ledger entry — the audit trail records what happened, not
        // what was attempted.
        Assert.Single(await check.LedgerOfAsync(userId));
    }

    [Fact]
    public async Task Spending_the_exact_balance_is_allowed()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();
        var wallet = new WalletService(context);

        await wallet.ApplyAsync(Credit(userId, currency.Id, 75));
        var result = await wallet.ApplyAsync(Debit(userId, currency.Id, -75));

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.Amount);
    }

    [Fact]
    public async Task Retired_currency_cannot_move()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync(enabled: false);

        var result = await new WalletService(context).ApplyAsync(Credit(userId, currency.Id, 10));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.CurrencyDisabled.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Unknown_currency_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await new WalletService(context).ApplyAsync(Credit(userId, Guid.NewGuid(), 10));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.CurrencyNotFound.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Zero_delta_is_rejected()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();

        var result = await new WalletService(context).ApplyAsync(Credit(userId, currency.Id, 0));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.InvalidAmount.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Currencies_are_held_independently()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var coins = await context.CreateCurrencyAsync();
        var gems = await context.CreateCurrencyAsync();
        var wallet = new WalletService(context);

        await wallet.ApplyAsync(Credit(userId, coins.Id, 100));
        await wallet.ApplyAsync(Credit(userId, gems.Id, 5));

        var balances = await wallet.GetBalancesAsync(userId);

        Assert.Equal(2, balances.Count);
        Assert.Equal(100, balances.Single(b => b.Currency == coins.Key).Amount);
        Assert.Equal(5, balances.Single(b => b.Currency == gems.Key).Amount);
    }

    [Fact]
    public async Task Balances_report_the_key_not_the_id()
    {
        // The Unity wallet reconciles on the key, so this is contract, not cosmetics.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync(key: "coins_wire_test");

        await new WalletService(context).ApplyAsync(Credit(userId, currency.Id, 1));

        var balances = await new WalletService(context).GetBalancesAsync(userId);
        Assert.Equal("coins_wire_test", Assert.Single(balances).Currency);
    }

    [Fact]
    public async Task Concurrent_credits_all_land()
    {
        await using var setup = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(setup);
        var currency = await setup.CreateCurrencyAsync();

        // Includes the very first credit, so the row-creation race is under test too — that is
        // the case HOLDLOCK exists for.
        const int operations = 40;
        var tasks = Enumerable.Range(0, operations).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await new WalletService(context).ApplyAsync(Credit(userId, currency.Id, 1));
        });

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.Succeeded, r.Errors.FirstOrDefault()));

        await using var check = _fixture.CreateContext();
        Assert.Equal(operations, await check.BalanceOfAsync(userId, currency.Id));
        Assert.Equal(operations, (await check.LedgerOfAsync(userId)).Count);

        // Exactly one balance row, and every BalanceAfter distinct — proof the mutations
        // serialised rather than interleaving on a stale read.
        Assert.Equal(1, await check.UserCurrencyBalances.CountAsync(b => b.UserId == userId));

        var balancesAfter = (await check.LedgerOfAsync(userId)).Select(e => e.BalanceAfter).ToList();
        Assert.Equal(balancesAfter.Count, balancesAfter.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, operations).Select(i => (long)i), balancesAfter.OrderBy(v => v));
    }

    [Fact]
    public async Task Concurrent_debits_cannot_overdraw()
    {
        await using var setup = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(setup);
        var currency = await setup.CreateCurrencyAsync();
        await new WalletService(setup).ApplyAsync(Credit(userId, currency.Id, 10));

        // Twenty racing attempts to spend 1 against a balance of 10: exactly ten may win.
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await new WalletService(context).ApplyAsync(Debit(userId, currency.Id, -1));
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(10, results.Count(r => r.Succeeded));
        Assert.Equal(10, results.Count(r => !r.Succeeded));

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.BalanceOfAsync(userId, currency.Id));
        Assert.All(await check.LedgerOfAsync(userId), e => Assert.True(e.BalanceAfter >= 0));
    }

    [Fact]
    public async Task Transaction_type_is_stored_as_readable_text()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();

        await new WalletService(context).ApplyAsync(new WalletMutation
        {
            UserId = userId,
            CurrencyId = currency.Id,
            Delta = 10,
            TransactionType = CurrencyTransactionType.AdminGrant,
            SourceType = LedgerSourceType.Admin
        });

        await using var check = _fixture.CreateContext();
        var stored = await check.Database
            .SqlQueryRaw<string>(
                "SELECT [TransactionType] AS [Value] FROM [CurrencyLedgerEntries] WHERE [UserId] = {0}",
                userId)
            .SingleAsync();

        Assert.Equal("ADMIN_GRANT", stored);

        // And round-trips back to the enum rather than silently degrading to Unknown.
        var entry = Assert.Single(await check.LedgerOfAsync(userId));
        Assert.Equal(CurrencyTransactionType.AdminGrant, entry.TransactionType);
        Assert.Equal(LedgerSourceType.Admin, entry.SourceType);
    }

    private static WalletMutation Credit(Guid userId, Guid currencyId, long amount) => new()
    {
        UserId = userId,
        CurrencyId = currencyId,
        Delta = amount,
        TransactionType = CurrencyTransactionType.LessonReward,
        SourceType = LedgerSourceType.ProgressAttempt
    };

    private static WalletMutation Debit(Guid userId, Guid currencyId, long amount) => new()
    {
        UserId = userId,
        CurrencyId = currencyId,
        Delta = amount,
        TransactionType = CurrencyTransactionType.Purchase,
        SourceType = LedgerSourceType.Purchase
    };
}
