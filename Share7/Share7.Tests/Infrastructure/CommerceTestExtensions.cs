using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Models;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Persistence;

namespace Share7.Tests.Infrastructure;

/// <summary>One thing a test product hands over. Kind is not here — it belongs to the product.</summary>
public record GrantSpecification(string Reference, int Quantity = 1);

public static class CommerceTestExtensions
{
    /// <summary>
    /// A kind to hang test products off. Named kinds are **get-or-create**: names are unique, the
    /// migration seeds <c>Cosmetic</c> and <c>Content Pack</c>, and every test class shares one
    /// database — so asking for a name twice has to return the same row rather than collide.
    /// <para>
    /// Kinds are read-only lookup data to almost every test, which is what makes sharing safe. A
    /// test that <em>renames or deletes</em> one must pass a name unique to itself.
    /// </para>
    /// </summary>
    public static async Task<ProductKind> CreateProductKindAsync(
        this ApplicationDbContext context,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        if (name is not null)
        {
            var existing = await context.ProductKinds
                .FirstOrDefaultAsync(k => k.Name == name, cancellationToken);

            if (existing is not null)
                return existing;
        }

        var kind = new ProductKind
        {
            Id = Guid.NewGuid(),
            Name = name ?? $"Kind{Guid.NewGuid():N}"[..12]
        };

        kind.Translations = await TranslateAsync(
            context, langId => new ProductKindTranslation { LangId = langId, Name = kind.Name }, cancellationToken);

        context.ProductKinds.Add(kind);
        await context.SaveChangesAsync(cancellationToken);
        return kind;
    }

    /// <summary>
    /// One row per configured language, which is what the services demand — a fixture that skipped a
    /// language would fail validation rather than the behaviour under test.
    /// </summary>
    private static async Task<List<T>> TranslateAsync<T>(
        ApplicationDbContext context,
        Func<Guid, T> build,
        CancellationToken cancellationToken)
    {
        var languageIds = await context.Languages
            .AsNoTracking()
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        return languageIds.Select(build).ToList();
    }

    /// <summary>
    /// A product with a key unique to this test. Creates its kind too unless one is supplied —
    /// a product cannot exist without one.
    /// </summary>
    public static async Task<Product> CreateProductAsync(
        this ApplicationDbContext context,
        GrantSpecification[]? grants = null,
        bool active = true,
        string? key = null,
        Guid? productKindId = null,
        CancellationToken cancellationToken = default)
    {
        var kindId = productKindId
            ?? (await context.CreateProductKindAsync(cancellationToken: cancellationToken)).Id;

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Key = key ?? $"p_{Guid.NewGuid():N}"[..20],
            Active = active,
            ProductKindId = kindId
        };

        product.Translations = await TranslateAsync(
            context,
            langId => new ProductTranslation { LangId = langId, Name = "Test Product" },
            cancellationToken);

        foreach (var grant in grants ?? [new GrantSpecification($"cos_{Guid.NewGuid():N}"[..16])])
        {
            product.Grants.Add(new ProductGrant
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Reference = grant.Reference,
                Quantity = grant.Quantity
            });
        }

        context.Products.Add(product);
        await context.SaveChangesAsync(cancellationToken);
        return product;
    }

    /// <summary>
    /// The same text in every configured language — enough to satisfy the "a name for every
    /// language" rule when a test is about something else. Tests *about* that rule build their own
    /// partial arrays.
    /// </summary>
    public static async Task<List<CommerceTranslationRequest>> TranslationsAsync(
        this ApplicationDbContext context,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var languageIds = await context.Languages
            .AsNoTracking()
            .Select(l => l.Id)
            .ToListAsync(cancellationToken);

        return languageIds
            .Select(id => new CommerceTranslationRequest { LangId = id, Name = name, Description = description })
            .ToList();
    }

    /// <summary>An offer selling the given products, translated into every configured language.</summary>
    public static async Task<Offer> CreateOfferAsync(
        this ApplicationDbContext context,
        Guid currencyId,
        Guid[] productIds,
        long price = 100,
        long? originalPrice = null,
        OfferAvailability availability = OfferAvailability.Available,
        int? purchaseLimit = null,
        DateTime? expiresAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var offer = new Offer
        {
            Id = Guid.NewGuid(),
            CurrencyId = currencyId,
            Price = price,
            OriginalPrice = originalPrice,
            Availability = availability,
            PurchaseLimit = purchaseLimit,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Products = productIds.Select(id => new OfferProduct { ProductId = id }).ToList()
        };

        offer.Translations = await TranslateAsync(
            context, langId => new OfferTranslation { LangId = langId, Name = "Test Offer" }, cancellationToken);

        context.Offers.Add(offer);
        await context.SaveChangesAsync(cancellationToken);
        return offer;
    }

    public static Task<List<PurchaseTransaction>> TransactionsOfAsync(
        this ApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        context.PurchaseTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public static Task<List<Entitlement>> EntitlementsOfAsync(
        this ApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        context.Entitlements
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.GrantedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Walks the chain the commerce contract specifies — <c>Entitlement → Product → ProductGrant</c>
    /// — which is what has to keep working after the product leaves the shop.
    /// </summary>
    public static async Task<List<ProductGrant>> ResolveGrantsAsync(
        this ApplicationDbContext context,
        Guid entitlementId,
        CancellationToken cancellationToken = default)
    {
        var productId = await context.Entitlements
            .AsNoTracking()
            .Where(e => e.Id == entitlementId)
            .Select(e => e.ProductId)
            .SingleAsync(cancellationToken);

        return await context.ProductGrants
            .AsNoTracking()
            .Where(g => g.ProductId == productId)
            .OrderBy(g => g.Reference)
            .ToListAsync(cancellationToken);
    }
}
