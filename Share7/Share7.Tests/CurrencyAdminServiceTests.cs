using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Models;
using Share7.Domain.Economy;
using Share7.Infrastructure.Economy;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class CurrencyAdminServiceTests
{
    private readonly SqlServerFixture _fixture;

    public CurrencyAdminServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Grant_credits_the_caller_from_the_token()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();

        var result = await Service(context).GrantAsync(userId, new AdminGrantCurrencyRequest
        {
            CurrencyId = currency.Id,
            Amount = 250,
            Reason = "seeded for testing"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(250, result.Value!.Amount);

        await using var check = _fixture.CreateContext();
        Assert.Equal(250, await check.BalanceOfAsync(userId, currency.Id));
    }

    [Fact]
    public async Task Grant_cannot_touch_anyone_elses_balance()
    {
        // The request has no user field at all, so the only account it can reach is the caller's.
        // This asserts the property rather than the absence of a property, so it keeps holding if
        // a target field is ever reintroduced without the matching role check.
        await using var context = _fixture.CreateContext();
        var caller = await TestData.CreateUserAsync(context);
        var bystander = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();

        await Service(context).GrantAsync(caller, new AdminGrantCurrencyRequest
        {
            CurrencyId = currency.Id,
            Amount = 100
        });

        await using var check = _fixture.CreateContext();
        Assert.Equal(100, await check.BalanceOfAsync(caller, currency.Id));
        Assert.Equal(0, await check.BalanceOfAsync(bystander, currency.Id));
        Assert.Empty(await check.LedgerOfAsync(bystander));
    }

    [Fact]
    public async Task Positive_and_negative_grants_get_different_ledger_types()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();
        var service = Service(context);

        await service.GrantAsync(userId, new AdminGrantCurrencyRequest { CurrencyId = currency.Id, Amount = 100 });
        await service.GrantAsync(userId, new AdminGrantCurrencyRequest { CurrencyId = currency.Id, Amount = -40 });

        await using var check = _fixture.CreateContext();
        var ledger = await check.LedgerOfAsync(userId);

        Assert.Equal(CurrencyTransactionType.AdminGrant, ledger[0].TransactionType);
        Assert.Equal(CurrencyTransactionType.AdminAdjustment, ledger[1].TransactionType);
        Assert.Equal(60, await check.BalanceOfAsync(userId, currency.Id));
    }

    [Fact]
    public async Task Reason_is_recorded_on_the_ledger_entry()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();

        await Service(context).GrantAsync(userId, new AdminGrantCurrencyRequest
        {
            CurrencyId = currency.Id,
            Amount = 10,
            Reason = "compensation for lost progress"
        });

        await using var check = _fixture.CreateContext();
        var entry = Assert.Single(await check.LedgerOfAsync(userId));

        Assert.Contains("compensation for lost progress", entry.Metadata);
        Assert.Equal(userId.ToString(), entry.SourceId);
        Assert.Equal(LedgerSourceType.Admin, entry.SourceType);
    }

    [Fact]
    public async Task Deducting_more_than_the_balance_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();
        var service = Service(context);

        await service.GrantAsync(userId, new AdminGrantCurrencyRequest { CurrencyId = currency.Id, Amount = 30 });
        var result = await service.GrantAsync(userId, new AdminGrantCurrencyRequest { CurrencyId = currency.Id, Amount = -31 });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.InsufficientBalance.Code, result.Error!.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(30, await check.BalanceOfAsync(userId, currency.Id));
    }

    [Fact]
    public async Task Zero_amount_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var currency = await context.CreateCurrencyAsync();

        var result = await Service(context).GrantAsync(userId, new AdminGrantCurrencyRequest
        {
            CurrencyId = currency.Id,
            Amount = 0
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.InvalidAmount.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Granting_to_a_deleted_account_is_refused()
    {
        // An access token outlives deletion by up to its lifetime, so this is reachable.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();

        var result = await Service(context).GrantAsync(Guid.NewGuid(), new AdminGrantCurrencyRequest
        {
            CurrencyId = currency.Id,
            Amount = 10
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task Currency_keys_are_unique_and_immutable()
    {
        await using var context = _fixture.CreateContext();
        var service = Service(context);
        var key = $"k{Guid.NewGuid():N}"[..12];

        var created = await service.CreateAsync(new CreateCurrencyRequest { Key = key, Name = "Coins" });
        Assert.True(created.Succeeded);

        var duplicate = await service.CreateAsync(new CreateCurrencyRequest { Key = key, Name = "Other" });
        Assert.False(duplicate.Succeeded);
        Assert.Equal(ApiErrors.CurrencyKeyTaken.Code, duplicate.Error!.Code);

        // Update changes the display fields; the key is not updatable at all.
        var updated = await service.UpdateAsync(created.Value!.CurrencyId, new UpdateCurrencyRequest
        {
            Name = "Renamed",
            Enabled = false
        });

        Assert.True(updated.Succeeded);
        Assert.Equal("Renamed", updated.Value!.Name);
        Assert.Equal(key, updated.Value.Key);
        Assert.False(updated.Value.Enabled);
    }

    [Fact]
    public async Task Retired_currency_refuses_further_grants()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = Service(context);

        var created = await service.CreateAsync(new CreateCurrencyRequest
        {
            Key = $"r{Guid.NewGuid():N}"[..12],
            Name = "Retiring"
        });

        await service.UpdateAsync(created.Value!.CurrencyId, new UpdateCurrencyRequest
        {
            Name = "Retiring",
            Enabled = false
        });

        var result = await service.GrantAsync(userId, new AdminGrantCurrencyRequest
        {
            CurrencyId = created.Value.CurrencyId,
            Amount = 10
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.CurrencyDisabled.Code, result.Error!.Code);
    }

    // Fully qualified: an unqualified "Infrastructure" binds to Share7.Tests.Infrastructure here.
    private static CurrencyAdminService Service(Share7.Infrastructure.Persistence.ApplicationDbContext context) =>
        new(context, new WalletService(context));
}
