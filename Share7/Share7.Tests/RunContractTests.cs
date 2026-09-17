using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Runs.Models;
using Share7.Domain.Economy;
using Share7.Domain.Runs;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The two guarantees that no service test can protect, because both fail silently.
/// <para>
/// A request shape that grows a currency field still passes every settlement test — the server would
/// simply have a number it is not obliged to read, until somebody reads it. And a unique index whose
/// filter excludes the rows it was meant to constrain still passes every sequential test, because the
/// service's own checks catch those cases; only a duplicate that actually inserts reveals that the
/// last line of defence was never there.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RunContractTests
{
    private readonly SqlServerFixture _fixture;

    public RunContractTests(SqlServerFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Test 17, as reflection rather than a grep.
    /// <para>
    /// **The whole feature rests on this absence.** A 3D coin is a gameplay signal because there is
    /// nowhere in a request to say what it is worth; the moment there is, the client is authoritative
    /// again and BC-COM-04 reopens without a single line of settlement logic changing.
    /// </para>
    /// </summary>
    [Fact]
    public void No_run_request_shape_accepts_a_currency_amount()
    {
        string[] forbidden =
        [
            "currency", "amount", "balance", "reward", "payout", "price", "coin", "gem", "wallet"
        ];

        var requestTypes = typeof(StartRunRequest).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(StartRunRequest).Namespace
                        && t.IsClass
                        && (t.Name.EndsWith("Request", StringComparison.Ordinal)
                            || t.Name.EndsWith("Report", StringComparison.Ordinal)))
            .ToList();

        // A guard that matches nothing is worse than no guard, so prove it is actually looking at the
        // shapes it claims to police.
        Assert.Contains(typeof(StartRunRequest), requestTypes);
        Assert.Contains(typeof(SubmitRunResultRequest), requestTypes);
        Assert.Contains(typeof(RunSignalReport), requestTypes);
        Assert.Contains(typeof(RunModifierReport), requestTypes);

        var offenders = requestTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => new { Type = t.Name, Property = p.Name }))
            .Where(p => forbidden.Any(word =>
                p.Property.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(p => $"{p.Type}.{p.Property}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"A run request must never carry a currency amount. Offending members: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// The platform-default valuation rows are the ones every unconfigured mini-game resolves
    /// through, and EF's SQL Server provider filters a unique index over a nullable column to
    /// <c>IS NOT NULL</c> unless told otherwise — which would leave exactly those rows unconstrained.
    /// This writes straight to the database, bypassing the service, because that is the only way to
    /// tell a working index from a working service check.
    /// </summary>
    [Fact]
    public async Task Two_platform_default_prices_for_one_kind_cannot_both_exist()
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();
        var kind = $"k{Guid.NewGuid():N}"[..16];

        await context.CreateValuationAsync(coins.Id, kind: kind, gameId: null, unitValue: 1);

        await using var second = _fixture.CreateContext();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            second.CreateValuationAsync(coins.Id, kind: kind, gameId: null, unitValue: 99));
    }

    [Fact]
    public async Task A_default_and_a_game_specific_price_are_not_a_collision()
    {
        await using var context = _fixture.CreateContext();
        var coins = await context.CreateCurrencyAsync();
        var game = await context.CreateGameAsync();
        var kind = $"k{Guid.NewGuid():N}"[..16];

        await context.CreateValuationAsync(coins.Id, kind: kind, gameId: null, unitValue: 1);
        await context.CreateValuationAsync(coins.Id, kind: kind, gameId: game.Id, unitValue: 10);

        Assert.Equal(2, await context.SignalValuations.CountAsync(v => v.SignalKind == kind));
    }

    [Theory]
    [InlineData("coin", true)]
    [InlineData("chest_small", true)]
    [InlineData("mg147_starfish", true)]
    [InlineData("Coin", false)]
    [InlineData("coin-large", false)]
    [InlineData("1coin", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_pickup_kind_is_shaped_like_a_currency_key(string? kind, bool valid) =>
        Assert.Equal(valid, SignalKinds.IsValid(kind));

    [Fact]
    public void A_pickup_kind_normalises_case_so_one_prefab_resolves_one_row() =>
        Assert.Equal("coin", SignalKinds.Normalise("  Coin "));
}
