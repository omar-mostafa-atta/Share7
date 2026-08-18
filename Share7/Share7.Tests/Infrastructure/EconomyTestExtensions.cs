using Microsoft.EntityFrameworkCore;
using Share7.Domain.Economy;
using Share7.Infrastructure.Persistence;

namespace Share7.Tests.Infrastructure;

public static class EconomyTestExtensions
{
    /// <summary>A currency with a key unique to this test, so parallel classes cannot collide.</summary>
    public static async Task<Currency> CreateCurrencyAsync(
        this ApplicationDbContext context,
        string? key = null,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        var currency = new Currency
        {
            Id = Guid.NewGuid(),
            Key = key ?? $"c{Guid.NewGuid():N}"[..16],
            Name = "Test Currency",
            Enabled = enabled,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.Currencies.Add(currency);
        await context.SaveChangesAsync(cancellationToken);
        return currency;
    }

    public static Task<long> BalanceOfAsync(
        this ApplicationDbContext context,
        Guid userId,
        Guid currencyId,
        CancellationToken cancellationToken = default) =>
        context.UserCurrencyBalances
            .AsNoTracking()
            .Where(b => b.UserId == userId && b.CurrencyId == currencyId)
            .Select(b => b.Amount)
            .FirstOrDefaultAsync(cancellationToken);

    public static Task<List<CurrencyLedgerEntry>> LedgerOfAsync(
        this ApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        context.CurrencyLedgerEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);
}
