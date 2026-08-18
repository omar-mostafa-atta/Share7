using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Domain.Economy;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Economy;

public class CurrencyAdminService : ICurrencyAdminService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWalletService _wallet;

    public CurrencyAdminService(ApplicationDbContext dbContext, IWalletService wallet)
    {
        _dbContext = dbContext;
        _wallet = wallet;
    }

    public async Task<IReadOnlyList<CurrencyDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Currencies
            .AsNoTracking()
            .OrderBy(c => c.CreatedAtUtc)
            .Select(c => new CurrencyDto
            {
                CurrencyId = c.Id,
                Key = c.Key,
                Name = c.Name,
                Description = c.Description,
                Enabled = c.Enabled
            })
            .ToListAsync(cancellationToken);

    public async Task<ServiceResult<CurrencyDto>> CreateAsync(
        CreateCurrencyRequest request,
        CancellationToken cancellationToken = default)
    {
        var key = request.Key?.Trim().ToLowerInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;

        if (key.Length == 0)
            return ServiceResult<CurrencyDto>.Failure(
                ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Key is required.");

        if (name.Length == 0)
            return ServiceResult<CurrencyDto>.Failure(
                ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Name is required.");

        if (await _dbContext.Currencies.AnyAsync(c => c.Key == key, cancellationToken))
            return ServiceResult<CurrencyDto>.Failure(
                ApiErrors.CurrencyKeyTaken,
                ServiceErrorKind.Conflict,
                $"A currency with key '{key}' already exists.",
                new Dictionary<string, object?> { ["key"] = key });

        var currency = new Currency
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = name,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Enabled = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Currencies.Add(currency);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<CurrencyDto>.Success(ToDto(currency));
    }

    public async Task<ServiceResult<CurrencyDto>> UpdateAsync(
        Guid currencyId,
        UpdateCurrencyRequest request,
        CancellationToken cancellationToken = default)
    {
        var currency = await _dbContext.Currencies
            .FirstOrDefaultAsync(c => c.Id == currencyId, cancellationToken);

        if (currency is null)
            return ServiceResult<CurrencyDto>.Failure(
                ApiErrors.CurrencyNotFound, ServiceErrorKind.NotFound, "Currency not found.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return ServiceResult<CurrencyDto>.Failure(
                ApiErrors.ValidationFailed, ServiceErrorKind.Validation, "Name is required.");

        // Key is intentionally not updatable — the client caches balances against it.
        currency.Name = name;
        currency.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        currency.Enabled = request.Enabled;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<CurrencyDto>.Success(ToDto(currency));
    }

    public async Task<ServiceResult<WalletMutationResult>> GrantAsync(
        Guid userId,
        AdminGrantCurrencyRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Amount == 0)
            return ServiceResult<WalletMutationResult>.Failure(
                ApiErrors.InvalidAmount, ServiceErrorKind.Validation, "Amount must not be zero.");

        // The id comes from a validated token, so the account existed when it was issued — but a
        // token outlives deletion by up to its lifetime, so a deleted account can still reach
        // here. Caught explicitly rather than left to fail on the balance insert's foreign key.
        if (!await _dbContext.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return ServiceResult<WalletMutationResult>.Failure(
                ApiErrors.NotFound, ServiceErrorKind.NotFound, "User not found.");

        // A deduction is a correction rather than a grant, and the ledger should say which —
        // "ADMIN_GRANT -500" would read as a bug during an audit.
        var transactionType = request.Amount > 0
            ? CurrencyTransactionType.AdminGrant
            : CurrencyTransactionType.AdminAdjustment;

        return await _wallet.ApplyAsync(new WalletMutation
        {
            UserId = userId,
            CurrencyId = request.CurrencyId,
            Delta = request.Amount,
            TransactionType = transactionType,
            SourceType = LedgerSourceType.Admin,

            // Actor and target are the same account here. Recorded anyway so the entry still
            // names who performed it if this ever regains a target-user field.
            SourceId = userId.ToString(),
            Metadata = string.IsNullOrWhiteSpace(request.Reason)
                ? null
                : System.Text.Json.JsonSerializer.Serialize(new { reason = request.Reason.Trim() })
        }, cancellationToken);
    }

    private static CurrencyDto ToDto(Currency currency) => new()
    {
        CurrencyId = currency.Id,
        Key = currency.Key,
        Name = currency.Name,
        Description = currency.Description,
        Enabled = currency.Enabled
    };
}
