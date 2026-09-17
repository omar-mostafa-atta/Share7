using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Commerce;

public class OfferService : IOfferService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public OfferService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public Task<OffersResponse> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        BuildAsync(userId, onlyPurchasable: false, cancellationToken);

    public Task<OffersResponse> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        BuildAsync(userId, onlyPurchasable: true, cancellationToken);

    /// <summary>
    /// One reader for both shelves. The filter is applied **after** the same
    /// <see cref="OfferRules"/> evaluation the full listing and the purchase endpoint use, rather
    /// than as a second WHERE clause — an offer's expiry and the caller's purchase count are the
    /// same facts either way, and two places deciding "is this buyable" is how a shop starts
    /// offering something the purchase endpoint then refuses.
    /// </summary>
    private async Task<OffersResponse> BuildAsync(
        Guid userId,
        bool onlyPurchasable,
        CancellationToken cancellationToken = default)
    {
        var offers = await _dbContext.Offers
            .AsNoTracking()
            .Include(o => o.Currency)
            .Include(o => o.Translations)
            .Include(o => o.Products)
                .ThenInclude(op => op.Product!)
                    .ThenInclude(p => p.Kind)
            .Include(o => o.Products)
                .ThenInclude(op => op.Product!)
                    .ThenInclude(p => p.Grants)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);

        // How many times this caller has completed each offer. One query rather than one per offer,
        // because this is the shop's landing request.
        var purchaseCounts = await _dbContext.PurchaseTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.State == TransactionState.Completed)
            .GroupBy(t => t.OfferId)
            .Select(group => new { OfferId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.OfferId, x => x.Count, cancellationToken);

        // What the account already holds, so an offer that would hand over nothing is reported as
        // unbuyable here rather than only at the till.
        var owned = (await _dbContext.Entitlements
                .AsNoTracking()
                .Where(e => e.UserId == userId)
                .Select(e => e.ProductId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        // One clock reading for the whole response: two offers expiring in the same second must not
        // disagree because the loop took a moment.
        var now = DateTime.UtcNow;

        var dtos = offers
            .Select(offer => ToDto(
                offer,
                purchaseCounts.GetValueOrDefault(offer.Id),
                langId,
                now,
                OwnsEverything(offer, owned)))
            .ToList();

        if (onlyPurchasable)
        {
            dtos = dtos.Where(o => o.CanPurchase).ToList();

            var kept = dtos.Select(o => o.OfferId).ToHashSet();
            offers = offers.Where(o => kept.Contains(o.Id)).ToList();
        }

        // Flat lookup keyed by productId — a product in three bundles is described once, not three
        // times. Keeps the shop payload from growing quadratically with bundles.
        var products = offers
            .SelectMany(o => o.Products)
            .Select(op => op.Product!)
            .DistinctBy(p => p.Id)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(product => new ProductDto
            {
                ProductId = product.Id,
                Grants = CommerceMappings.ToClientDtos(product.Grants, product.Kind?.Name ?? string.Empty)
            })
            .ToList();

        return new OffersResponse { Offers = dtos, Products = products };
    }

    /// <summary>
    /// Guarded against the empty case: an offer with no products would vacuously "own everything"
    /// and vanish from the shop. Authoring refuses that, but the shop should not depend on it.
    /// </summary>
    private static bool OwnsEverything(Offer offer, HashSet<Guid> owned) =>
        offer.Products.Count > 0 && offer.Products.All(op => owned.Contains(op.ProductId));

    private static OfferDto ToDto(
        Offer offer,
        int purchaseCount,
        Guid langId,
        DateTime nowUtc,
        bool ownsEverything)
    {
        var eligibility = OfferRules.Evaluate(offer, purchaseCount, nowUtc, ownsEverything);

        // The caller's language, then English, then whatever exists — a name missing in one language
        // should show the other, not a blank row in the shop.
        var text = CommerceTranslationValidator.Resolve(offer.Translations, langId);

        return new OfferDto
        {
            OfferId = offer.Id,
            Name = text.Name,
            Description = text.Description,
            ProductIds = offer.Products
                .Select(op => op.ProductId)
                .OrderBy(id => id)
                .ToList(),
            Currency = offer.Currency?.Key ?? string.Empty,
            CurrencyId = offer.CurrencyId,
            Price = offer.Price,
            OriginalPrice = offer.OriginalPrice,
            Availability = eligibility.Availability,
            CanPurchase = eligibility.CanPurchase,
            IneligibleReasonKey = eligibility.ReasonKey,
            PurchaseLimit = offer.PurchaseLimit,
            PurchaseCount = purchaseCount,
            // SQL Server hands back datetime2 with Kind = Unspecified, which serializes without a
            // zone. The contract shows a trailing Z, so say so explicitly — same quirk as
            // grantedAtUtc and lastAttemptAt.
            ExpiresAtUtc = offer.ExpiresAtUtc is { } expiry
                ? DateTime.SpecifyKind(expiry, DateTimeKind.Utc)
                : null,
            SortOrder = offer.SortOrder,
            BadgeKey = offer.BadgeKey
        };
    }
}
