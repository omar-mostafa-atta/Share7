using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Domain.Economy;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Economy;

public class WalletService : IWalletService
{
    private readonly ApplicationDbContext _dbContext;

    public WalletService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<BalanceDto>> GetBalancesAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.UserCurrencyBalances
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Currency!.Key)
            .Select(b => new BalanceDto
            {
                Currency = b.Currency!.Key,
                Amount = b.Amount
            })
            .ToListAsync(cancellationToken);

    public async Task<ServiceResult<WalletMutationResult>> ApplyAsync(
        WalletMutation mutation,
        CancellationToken cancellationToken = default)
    {
        if (mutation.Delta == 0)
            return ServiceResult<WalletMutationResult>.Failure(
                ApiErrors.InvalidAmount,
                ServiceErrorKind.Validation,
                "A wallet mutation must move a non-zero amount.");

        var currency = await _dbContext.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == mutation.CurrencyId, cancellationToken);

        if (currency is null)
            return ServiceResult<WalletMutationResult>.Failure(
                ApiErrors.CurrencyNotFound,
                ServiceErrorKind.NotFound,
                $"Currency {mutation.CurrencyId} does not exist.");

        if (!currency.Enabled)
            return ServiceResult<WalletMutationResult>.Failure(
                ApiErrors.CurrencyDisabled,
                ServiceErrorKind.Validation,
                $"Currency '{currency.Key}' is retired and cannot be credited or debited.");

        // Join the caller's transaction when there is one — a purchase charges and grants as a
        // single unit, and opening a nested transaction here would let the charge commit
        // independently of the grant.
        var ambient = _dbContext.Database.CurrentTransaction;
        IDbContextTransaction? owned = null;

        if (ambient is null)
            owned = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await ApplyLockedAsync(mutation, currency, cancellationToken);

            if (owned is not null)
            {
                if (result.Succeeded)
                    await owned.CommitAsync(cancellationToken);
                else
                    await owned.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch
        {
            if (owned is not null)
                await owned.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (owned is not null)
                await owned.DisposeAsync();
        }
    }

    private async Task<ServiceResult<WalletMutationResult>> ApplyLockedAsync(
        WalletMutation mutation,
        Currency currency,
        CancellationToken cancellationToken)
    {
        // UPDLOCK serialises concurrent mutations of this one balance; HOLDLOCK extends it to a
        // key-range lock so that when the row does not exist yet, a second transaction cannot
        // insert it underneath us. Without HOLDLOCK the very first credit for a user races.
        var existing = await _dbContext.Database
            .SqlQueryRaw<long>(
                """
                SELECT [Amount] AS [Value]
                FROM [UserCurrencyBalances] WITH (UPDLOCK, HOLDLOCK)
                WHERE [UserId] = {0} AND [CurrencyId] = {1}
                """,
                mutation.UserId, mutation.CurrencyId)
            .ToListAsync(cancellationToken);

        var hasRow = existing.Count > 0;
        var current = hasRow ? existing[0] : 0L;
        var next = current + mutation.Delta;

        if (next < 0)
            return ServiceResult<WalletMutationResult>.Failure(
                ApiErrors.InsufficientBalance,
                ServiceErrorKind.Validation,
                $"Balance {current} is too low to deduct {-mutation.Delta} {currency.Key}.",
                new Dictionary<string, object?>
                {
                    ["currency"] = currency.Key,
                    ["balance"] = current,
                    ["required"] = -mutation.Delta
                });

        var now = DateTime.UtcNow;

        if (hasRow)
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE [UserCurrencyBalances]
                SET [Amount] = {0}, [UpdatedAtUtc] = {1}
                WHERE [UserId] = {2} AND [CurrencyId] = {3}
                """,
                [next, now, mutation.UserId, mutation.CurrencyId],
                cancellationToken);
        }
        else
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO [UserCurrencyBalances] ([Id], [UserId], [CurrencyId], [Amount], [UpdatedAtUtc])
                VALUES ({0}, {1}, {2}, {3}, {4})
                """,
                [Guid.NewGuid(), mutation.UserId, mutation.CurrencyId, next, now],
                cancellationToken);
        }

        var entry = new CurrencyLedgerEntry
        {
            UserId = mutation.UserId,
            CurrencyId = mutation.CurrencyId,
            Amount = mutation.Delta,
            BalanceAfter = next,
            TransactionType = mutation.TransactionType,
            SourceType = mutation.SourceType,
            SourceId = mutation.SourceId,
            IdempotencyKey = mutation.IdempotencyKey,
            Metadata = mutation.Metadata,
            CreatedAtUtc = now
        };

        _dbContext.CurrencyLedgerEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<WalletMutationResult>.Success(new WalletMutationResult
        {
            CurrencyId = currency.Id,
            Currency = currency.Key,
            Amount = next,
            LedgerEntryId = entry.Id
        });
    }
}
