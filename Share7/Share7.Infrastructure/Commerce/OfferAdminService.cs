using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Commerce;

public class OfferAdminService : IOfferAdminService
{
    private const int NameMaxLength = 128;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public OfferAdminService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<AdminOfferDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var offers = await Query()
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);

        var counts = await PurchaseCountsAsync(offers.Select(o => o.Id).ToList(), cancellationToken);
        var codes = await LanguageCodesAsync(cancellationToken);
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        var now = DateTime.UtcNow;

        return offers.Select(o => ToDto(o, counts.GetValueOrDefault(o.Id), codes, langId, now)).ToList();
    }

    public async Task<ServiceResult<AdminOfferDto>> GetAsync(
        Guid offerId,
        CancellationToken cancellationToken = default)
    {
        var offer = await Query().FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);

        if (offer is null)
            return NotFound(offerId);

        var counts = await PurchaseCountsAsync([offerId], cancellationToken);

        return ServiceResult<AdminOfferDto>.Success(ToDto(
            offer,
            counts.GetValueOrDefault(offerId),
            await LanguageCodesAsync(cancellationToken),
            await _languageService.ResolveCurrentAsync(cancellationToken),
            DateTime.UtcNow));
    }

    public async Task<ServiceResult<AdminOfferDto>> CreateAsync(
        CreateOfferRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, cancellationToken);

        if (!validated.Succeeded)
            return Rewrap(validated);

        var now = DateTime.UtcNow;

        var offer = new Offer
        {
            Id = Guid.NewGuid(),
            CurrencyId = request.CurrencyId,
            Price = request.Price,
            OriginalPrice = request.OriginalPrice,
            Availability = validated.Value!.Availability,
            PurchaseLimit = request.PurchaseLimit,
            ExpiresAtUtc = request.ExpiresAtUtc,
            SortOrder = request.SortOrder,
            BadgeKey = Blank(request.BadgeKey),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Translations = validated.Value.Translations
                .Select(t => new OfferTranslation { LangId = t.LangId, Name = t.Name, Description = t.Description })
                .ToList(),
            Products = validated.Value.ProductIds
                .Select(id => new OfferProduct { ProductId = id })
                .ToList()
        };

        _dbContext.Offers.Add(offer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ReadBackAsync(offer.Id, cancellationToken);
    }

    public async Task<ServiceResult> DeleteAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        var offer = await _dbContext.Offers.FirstOrDefaultAsync(o => o.Id == offerId, cancellationToken);

        // Idempotent: an offer that is already gone is the state the caller asked for.
        if (offer is null)
            return ServiceResult.Success();

        // Any transaction, not just completed ones — a refusal is history too, and its foreign key
        // is Restrict, so the database would refuse this as a raw SqlException otherwise.
        var transactionCount = await _dbContext.PurchaseTransactions
            .CountAsync(t => t.OfferId == offerId, cancellationToken);

        if (transactionCount > 0)
            return ServiceResult.Failure(
                ApiErrors.OfferPurchased,
                ServiceErrorKind.Conflict,
                $"{transactionCount} transaction(s) reference this offer, so it cannot be deleted — set availability to UNAVAILABLE to take it off sale.",
                new Dictionary<string, object?> { ["transactionCount"] = transactionCount });

        // Translations and the product links cascade; the products themselves do not.
        _dbContext.Offers.Remove(offer);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult.Success();
    }

    // ------------------------------------------------------------- validation

    private record ValidatedOffer(
        OfferAvailability Availability,
        List<CommerceName> Translations,
        List<Guid> ProductIds);

    private async Task<ServiceResult<ValidatedOffer>> ValidateAsync(
        CreateOfferRequest request,
        CancellationToken cancellationToken)
    {
        if (!WireEnum.TryFromWire<OfferAvailability>(request.Availability, out var availability)
            || availability == OfferAvailability.Unknown)
            return Invalid<ValidatedOffer>(
                $"'{request.Availability}' is not an availability. Valid values: AVAILABLE, UNAVAILABLE.");

        if (request.Price < 0)
            return Invalid<ValidatedOffer>("A price cannot be negative.");

        // A struck-through price below the real one reads as a price rise dressed up as a discount.
        if (request.OriginalPrice is { } original && original <= request.Price)
            return Invalid<ValidatedOffer>(
                $"originalPrice ({original}) must be above price ({request.Price}), or omitted when there is no discount.");

        if (request.PurchaseLimit is { } limit && limit < 1)
            return Invalid<ValidatedOffer>("A purchase limit must be at least 1. Omit it for unlimited.");

        var currency = await _dbContext.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CurrencyId, cancellationToken);

        if (currency is null)
            return ServiceResult<ValidatedOffer>.Failure(
                ApiErrors.CurrencyNotFound,
                ServiceErrorKind.NotFound,
                $"Currency {request.CurrencyId} does not exist.");

        if (!currency.Enabled)
            return ServiceResult<ValidatedOffer>.Failure(
                ApiErrors.CurrencyDisabled,
                ServiceErrorKind.Validation,
                $"Currency '{currency.Key}' is retired, so nothing can be priced in it.");

        var productIds = request.ProductIds.Distinct().ToList();

        if (productIds.Count == 0)
            return Invalid<ValidatedOffer>("An offer must sell at least one product.");

        var found = await _dbContext.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var missing = productIds.Except(found).ToList();

        if (missing.Count > 0)
            return ServiceResult<ValidatedOffer>.Failure(
                ApiErrors.ProductNotFound,
                ServiceErrorKind.NotFound,
                $"No such product(s): {string.Join(", ", missing)}.",
                new Dictionary<string, object?> { ["productIds"] = missing.Select(id => id.ToString()).ToList() });

        var translations = await CommerceTranslationValidator.ValidateAsync(
            _dbContext, request.Translations, ApiErrors.OfferInvalid, NameMaxLength, cancellationToken);

        if (!translations.Succeeded)
            return Rewrap<ValidatedOffer, List<CommerceName>>(translations);

        return ServiceResult<ValidatedOffer>.Success(
            new ValidatedOffer(availability, translations.Value!, productIds));
    }

    // ------------------------------------------------------------- helpers

    private IQueryable<Offer> Query() =>
        _dbContext.Offers
            .AsNoTracking()
            .Include(o => o.Currency)
            .Include(o => o.Translations)
            .Include(o => o.Products)
                .ThenInclude(op => op.Product!)
                    .ThenInclude(p => p.Kind)
            .Include(o => o.Products)
                .ThenInclude(op => op.Product!)
                    .ThenInclude(p => p.Grants)
            .Include(o => o.Products)
                .ThenInclude(op => op.Product!)
                    .ThenInclude(p => p.Translations);

    private Task<Dictionary<Guid, int>> PurchaseCountsAsync(
        List<Guid> offerIds,
        CancellationToken cancellationToken) =>
        _dbContext.PurchaseTransactions
            .AsNoTracking()
            .Where(t => offerIds.Contains(t.OfferId) && t.State == TransactionState.Completed)
            .GroupBy(t => t.OfferId)
            .Select(group => new { OfferId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.Count, cancellationToken);

    private Task<Dictionary<Guid, string>> LanguageCodesAsync(CancellationToken cancellationToken) =>
        _dbContext.Languages.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);

    private async Task<ServiceResult<AdminOfferDto>> ReadBackAsync(Guid offerId, CancellationToken cancellationToken)
    {
        var saved = await Query().FirstAsync(o => o.Id == offerId, cancellationToken);
        var counts = await PurchaseCountsAsync([offerId], cancellationToken);

        return ServiceResult<AdminOfferDto>.Success(ToDto(
            saved,
            counts.GetValueOrDefault(offerId),
            await LanguageCodesAsync(cancellationToken),
            await _languageService.ResolveCurrentAsync(cancellationToken),
            DateTime.UtcNow));
    }

    private static AdminOfferDto ToDto(
        Offer offer,
        int purchaseCount,
        IReadOnlyDictionary<Guid, string> languageCodes,
        Guid langId,
        DateTime nowUtc)
    {
        var text = CommerceTranslationValidator.Resolve(offer.Translations, langId);

        return new AdminOfferDto
        {
            OfferId = offer.Id,
            Name = text.Name,
            Description = text.Description,
            CurrencyId = offer.CurrencyId,
            Currency = offer.Currency?.Key ?? string.Empty,
            Price = offer.Price,
            OriginalPrice = offer.OriginalPrice,
            Availability = WireEnum.ToWire(offer.Availability),
            PurchaseLimit = offer.PurchaseLimit,
            ExpiresAtUtc = offer.ExpiresAtUtc is { } expiry ? DateTime.SpecifyKind(expiry, DateTimeKind.Utc) : null,
            SortOrder = offer.SortOrder,
            BadgeKey = offer.BadgeKey,
            PurchaseCount = purchaseCount,
            Expired = offer.ExpiresAtUtc is { } e && e <= nowUtc,
            CreatedAtUtc = DateTime.SpecifyKind(offer.CreatedAtUtc, DateTimeKind.Utc),
            UpdatedAtUtc = DateTime.SpecifyKind(offer.UpdatedAtUtc, DateTimeKind.Utc),
            Products = offer.Products
                .Where(op => op.Product is not null)
                .OrderBy(op => op.Product!.Key, StringComparer.Ordinal)
                .Select(op => ToProductDto(op.Product!, langId))
                .ToList()
        };
    }

    private static AdminOfferProductDto ToProductDto(Product product, Guid langId)
    {
        var kindName = product.Kind?.Name ?? string.Empty;
        var text = CommerceTranslationValidator.Resolve(product.Translations, langId);

        return new AdminOfferProductDto
        {
            ProductId = product.Id,
            Key = product.Key,
            Name = text.Name,
            Kind = ProductKindName.ToWire(kindName),
            Active = product.Active,
            GrantCount = product.Grants.Count,
            Grants = CommerceMappings.ToClientDtos(product.Grants, kindName)
        };
    }

    private static ServiceResult<AdminOfferDto> NotFound(Guid offerId) =>
        ServiceResult<AdminOfferDto>.Failure(
            ApiErrors.OfferNotFound,
            ServiceErrorKind.NotFound,
            $"Offer {offerId} does not exist.");

    private static ServiceResult<T> Invalid<T>(string message) =>
        ServiceResult<T>.Failure(ApiErrors.OfferInvalid, ServiceErrorKind.Validation, message);

    private static string? Blank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Carries a failure across result types without losing the code or details.</summary>
    private static ServiceResult<AdminOfferDto> Rewrap<T>(ServiceResult<T> failure) => new()
    {
        ErrorKind = failure.ErrorKind,
        Error = failure.Error,
        Errors = failure.Errors,
        Details = failure.Details
    };

    private static ServiceResult<TTarget> Rewrap<TTarget, TSource>(ServiceResult<TSource> failure) => new()
    {
        ErrorKind = failure.ErrorKind,
        Error = failure.Error,
        Errors = failure.Errors,
        Details = failure.Details
    };
}
