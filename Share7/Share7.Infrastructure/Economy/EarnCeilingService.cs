using Microsoft.EntityFrameworkCore;
using Share7.Application.Economy.Interfaces;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Economy;

/// <summary>
/// The daily earning ceiling, over <c>DailyCurrencyLedger</c>.
/// <para>
/// The counter is written by whatever granted the currency, inside that grant's own transaction, so
/// the number this reads is never ahead of or behind the balances it is meant to bound.
/// </para>
/// </summary>
public class EarnCeilingService : IEarnCeilingService
{
    private readonly ApplicationDbContext _dbContext;

    public EarnCeilingService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyDictionary<Guid, long>> HeadroomAsync(
        Guid userId,
        IReadOnlyCollection<Guid> currencyIds,
        CancellationToken cancellationToken = default)
    {
        if (currencyIds.Count == 0)
            return new Dictionary<Guid, long>();

        var ids = currencyIds.ToList();

        var caps = await _dbContext.Currencies
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.DailyEarnCap })
            .ToListAsync(cancellationToken);

        var day = DateTime.UtcNow.Date;

        var earned = await _dbContext.DailyCurrencyLedger
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.DayUtc == day && ids.Contains(l.CurrencyId))
            .ToDictionaryAsync(l => l.CurrencyId, l => l.EarnedAmount, cancellationToken);

        var headroom = new Dictionary<Guid, long>(caps.Count);

        foreach (var currency in caps)
        {
            if (currency.DailyEarnCap is not { } cap)
            {
                // No ceiling configured. MaxValue rather than a null the callers would each have to
                // remember to special-case — an uncapped currency is one with infinite headroom, and
                // saying so in the same type keeps the clamp arithmetic uniform.
                headroom[currency.Id] = long.MaxValue;
                continue;
            }

            var already = earned.GetValueOrDefault(currency.Id);

            // Floored at zero: a cap lowered below what somebody already earned today reads as
            // "nothing left", never as a debt they have to work off.
            headroom[currency.Id] = Math.Max(0, cap - already);
        }

        return headroom;
    }

    public async Task AccrueAsync(
        Guid userId,
        Guid currencyId,
        long amount,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            return;

        var day = nowUtc.Date;

        // UPDLOCK serialises two settlements racing on this counter; HOLDLOCK extends it to a key
        // range so the day's first two earnings cannot both decide the row does not exist and both
        // insert. Exactly the lock WalletService takes on a balance, for exactly the same reason.
        var existing = await _dbContext.Database
            .SqlQueryRaw<long>(
                """
                SELECT [EarnedAmount] AS [Value]
                FROM [DailyCurrencyLedger] WITH (UPDLOCK, HOLDLOCK)
                WHERE [UserId] = {0} AND [CurrencyId] = {1} AND [DayUtc] = {2}
                """,
                userId, currencyId, day)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE [DailyCurrencyLedger]
                SET [EarnedAmount] = [EarnedAmount] + {0}, [RunCount] = [RunCount] + 1, [UpdatedAtUtc] = {1}
                WHERE [UserId] = {2} AND [CurrencyId] = {3} AND [DayUtc] = {4}
                """,
                [amount, nowUtc, userId, currencyId, day],
                cancellationToken);

            return;
        }

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO [DailyCurrencyLedger] ([UserId], [CurrencyId], [DayUtc], [EarnedAmount], [RunCount], [UpdatedAtUtc])
            VALUES ({0}, {1}, {2}, {3}, 1, {4})
            """,
            [userId, currencyId, day, amount, nowUtc],
            cancellationToken);
    }
}
