using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Models;
using Share7.Domain.Commerce;
using Share7.Domain.Constants;
using Share7.Domain.Economy;
using Share7.Infrastructure.Commerce;
using Share7.Infrastructure.Economy;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class OfferServiceTests
{
    private readonly SqlServerFixture _fixture;

    public OfferServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_offer_reports_its_price_currency_key_and_every_product_it_sells()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var kind = await context.CreateProductKindAsync("Cosmetic");
        var first = await context.CreateProductAsync([new GrantSpecification("cos_a")], productKindId: kind.Id);
        var second = await context.CreateProductAsync([new GrantSpecification("cos_b", 2)], productKindId: kind.Id);

        var offer = await context.CreateOfferAsync(
            currency.Id, [first.Id, second.Id], price: 100, originalPrice: 150);

        var userId = await TestData.CreateUserAsync(context);
        var response = await Offers(context).GetForUserAsync(userId);

        var dto = response.Offers.Single(o => o.OfferId == offer.Id);

        // The client matches balances on the key, never the row id — so both are reported, and the
        // key is the one that pairs with GET /api/commerce/balances.
        Assert.Equal(currency.Key, dto.Currency);
        Assert.Equal(currency.Id, dto.CurrencyId);
        Assert.Equal(100, dto.Price);
        Assert.Equal(150, dto.OriginalPrice);
        Assert.Equal("AVAILABLE", dto.Availability);
        Assert.True(dto.CanPurchase);
        Assert.Null(dto.IneligibleReasonKey);
        Assert.Equal(2, dto.ProductIds.Count);

        // Grants live in the flat products lookup, not repeated per offer.
        var products = response.Products.Where(p => dto.ProductIds.Contains(p.ProductId)).ToList();
        Assert.Equal(2, products.Count);
        Assert.All(products, p => Assert.All(p.Grants, g => Assert.Equal("COSMETIC", g.Kind)));
        Assert.Equal(2, products.SelectMany(p => p.Grants).Single(g => g.Reference == "cos_b").Quantity);
    }

    [Fact]
    public async Task A_product_sold_by_two_offers_is_described_once()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();

        await context.CreateOfferAsync(currency.Id, [product.Id], price: 100);
        await context.CreateOfferAsync(currency.Id, [product.Id], price: 80);

        var userId = await TestData.CreateUserAsync(context);
        var response = await Offers(context).GetForUserAsync(userId);

        Assert.Single(response.Products, p => p.ProductId == product.Id);
    }

    [Fact]
    public async Task An_expired_offer_is_still_listed_but_cannot_be_bought()
    {
        // Listed rather than hidden: an entry vanishing from the shop looks like a bug to a player,
        // while a greyed-out one explains itself.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();
        var offer = await context.CreateOfferAsync(
            currency.Id, [product.Id], expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var userId = await TestData.CreateUserAsync(context);
        var dto = (await Offers(context).GetForUserAsync(userId)).Offers.Single(o => o.OfferId == offer.Id);

        Assert.Equal("EXPIRED", dto.Availability);
        Assert.False(dto.CanPurchase);
        Assert.Equal("commerce.offer.expired", dto.IneligibleReasonKey);

        // UTC with a trailing Z, unlike the older progress timestamps.
        Assert.Equal(DateTimeKind.Utc, dto.ExpiresAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task An_unavailable_offer_reports_disabled()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();
        var offer = await context.CreateOfferAsync(
            currency.Id, [product.Id], availability: OfferAvailability.Unavailable);

        var userId = await TestData.CreateUserAsync(context);
        var dto = (await Offers(context).GetForUserAsync(userId)).Offers.Single(o => o.OfferId == offer.Id);

        Assert.Equal("DISABLED", dto.Availability);
        Assert.False(dto.CanPurchase);
        Assert.Equal("commerce.offer.unavailable", dto.IneligibleReasonKey);
    }

    [Fact]
    public async Task Purchase_count_and_limit_are_resolved_for_the_caller()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100, purchaseLimit: 1);

        var buyer = await TestData.CreateUserAsync(context);
        var onlooker = await TestData.CreateUserAsync(context);
        await Credit(context, buyer, currency.Id, 500);

        var purchase = new PurchaseService(context, new WalletService(context), new EntitlementService(context));
        Assert.True((await purchase.PurchaseAsync(
            buyer, new PurchaseRequest { OfferId = offer.Id, RequestId = $"r_{Guid.NewGuid():N}" })).Succeeded);

        var buyersView = (await Offers(context).GetForUserAsync(buyer)).Offers.Single(o => o.OfferId == offer.Id);
        var othersView = (await Offers(context).GetForUserAsync(onlooker)).Offers.Single(o => o.OfferId == offer.Id);

        Assert.Equal(1, buyersView.PurchaseCount);
        Assert.False(buyersView.CanPurchase);
        Assert.Equal("PURCHASE_LIMIT_REACHED", buyersView.Availability);

        // Same offer, same moment, different account: entirely purchasable.
        Assert.Equal(0, othersView.PurchaseCount);
        Assert.True(othersView.CanPurchase);
    }

    [Fact]
    public async Task Too_few_coins_does_not_grey_an_offer_out()
    {
        // Deliberate: a student should be able to see what they are saving towards. Affordability is
        // decided at purchase time, and the client already knows both the price and the balance.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 10_000);

        var userId = await TestData.CreateUserAsync(context);
        var dto = (await Offers(context).GetForUserAsync(userId)).Offers.Single(o => o.OfferId == offer.Id);

        Assert.True(dto.CanPurchase);
        Assert.Equal("AVAILABLE", dto.Availability);
    }

    [Fact]
    public async Task Offers_come_back_in_the_callers_language()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();

        var offer = await context.CreateOfferAsync(currency.Id, [product.Id]);

        // Retitle per language directly, since the fixture writes one name into both.
        await context.OfferTranslations
            .Where(t => t.OfferId == offer.Id && t.LangId == LanguageIds.Arabic)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Name, "حزمة البداية"));

        var userId = await TestData.CreateUserAsync(context);

        await using var arabic = _fixture.CreateContext();
        var dto = (await Offers(arabic, LanguageIds.Arabic).GetForUserAsync(userId))
            .Offers.Single(o => o.OfferId == offer.Id);

        Assert.Equal("حزمة البداية", dto.Name);
    }

    [Fact]
    public async Task Todays_shelf_omits_what_the_full_listing_greys_out()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();

        var live = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100);
        var expired = await context.CreateOfferAsync(
            currency.Id, [product.Id], expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        var off = await context.CreateOfferAsync(
            currency.Id, [product.Id], availability: OfferAvailability.Unavailable);

        var userId = await TestData.CreateUserAsync(context);
        var service = Offers(context);

        var everything = (await service.GetForUserAsync(userId)).Offers.Select(o => o.OfferId).ToList();
        var today = (await service.GetActiveForUserAsync(userId)).Offers.Select(o => o.OfferId).ToList();

        // The full listing shows all three so the client can grey two of them out.
        Assert.Contains(live.Id, everything);
        Assert.Contains(expired.Id, everything);
        Assert.Contains(off.Id, everything);

        // Today's shelf shows only what can actually be bought.
        Assert.Contains(live.Id, today);
        Assert.DoesNotContain(expired.Id, today);
        Assert.DoesNotContain(off.Id, today);

        var shelf = (await service.GetActiveForUserAsync(userId)).Offers;
        Assert.All(shelf, o => Assert.True(o.CanPurchase));
        Assert.All(shelf, o => Assert.Null(o.IneligibleReasonKey));
    }

    [Fact]
    public async Task Todays_shelf_still_lists_what_the_caller_cannot_afford()
    {
        // Affordability is decided at purchase time, not by hiding the offer — otherwise a student
        // can never see what they are saving towards.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 10_000);

        var userId = await TestData.CreateUserAsync(context);

        Assert.Contains(
            (await Offers(context).GetActiveForUserAsync(userId)).Offers,
            o => o.OfferId == offer.Id);
    }

    [Fact]
    public async Task Todays_shelf_drops_an_offer_once_this_account_hits_its_limit()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100, purchaseLimit: 1);

        var buyer = await TestData.CreateUserAsync(context);
        var onlooker = await TestData.CreateUserAsync(context);
        await Credit(context, buyer, currency.Id, 500);

        var purchase = new PurchaseService(context, new WalletService(context), new EntitlementService(context));
        Assert.True((await purchase.PurchaseAsync(
            buyer, new PurchaseRequest { OfferId = offer.Id, RequestId = $"r_{Guid.NewGuid():N}" })).Succeeded);

        Assert.DoesNotContain(
            (await Offers(context).GetActiveForUserAsync(buyer)).Offers, o => o.OfferId == offer.Id);

        // Same offer, same moment, an account that has not bought it: still on the shelf.
        Assert.Contains(
            (await Offers(context).GetActiveForUserAsync(onlooker)).Offers, o => o.OfferId == offer.Id);
    }

    [Fact]
    public async Task An_offer_the_account_already_owns_in_full_leaves_the_shelf()
    {
        // Otherwise the shop advertises something the purchase endpoint then refuses as
        // ALREADY_OWNED — a listing that disagrees with the till.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100);

        var userId = await TestData.CreateUserAsync(context);
        await Credit(context, userId, currency.Id, 500);

        var purchase = new PurchaseService(context, new WalletService(context), new EntitlementService(context));
        Assert.True((await purchase.PurchaseAsync(
            userId, new PurchaseRequest { OfferId = offer.Id, RequestId = $"r_{Guid.NewGuid():N}" })).Succeeded);

        Assert.DoesNotContain(
            (await Offers(context).GetActiveForUserAsync(userId)).Offers, o => o.OfferId == offer.Id);

        // Still in the full listing, explaining itself.
        var listed = (await Offers(context).GetForUserAsync(userId)).Offers.Single(o => o.OfferId == offer.Id);
        Assert.False(listed.CanPurchase);
        Assert.Equal("NOT_ELIGIBLE", listed.Availability);
        Assert.Equal("commerce.offer.already_owned", listed.IneligibleReasonKey);
    }

    [Fact]
    public async Task A_bundle_only_half_owned_stays_on_the_shelf()
    {
        // The rest of the bundle is still worth paying for, so it must stay buyable.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var kind = await context.CreateProductKindAsync();
        var owned = await context.CreateProductAsync(productKindId: kind.Id);
        var wanted = await context.CreateProductAsync(productKindId: kind.Id);
        var offer = await context.CreateOfferAsync(currency.Id, [owned.Id, wanted.Id], price: 100);

        var userId = await TestData.CreateUserAsync(context);
        await new EntitlementService(context).GrantAsync(userId, owned.Id, EntitlementSource.AdminGrant);

        Assert.Contains(
            (await Offers(context).GetActiveForUserAsync(userId)).Offers, o => o.OfferId == offer.Id);
    }

    [Fact]
    public async Task Todays_shelf_only_describes_products_it_still_lists()
    {
        // The flat products lookup has to shrink with the offers, or the payload carries catalogue
        // entries for things the caller was not shown.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var shown = await context.CreateProductAsync();
        var hidden = await context.CreateProductAsync();

        await context.CreateOfferAsync(currency.Id, [shown.Id], price: 100);
        await context.CreateOfferAsync(currency.Id, [hidden.Id], availability: OfferAvailability.Unavailable);

        var userId = await TestData.CreateUserAsync(context);
        var today = await Offers(context).GetActiveForUserAsync(userId);

        Assert.Contains(today.Products, p => p.ProductId == shown.Id);
        Assert.DoesNotContain(today.Products, p => p.ProductId == hidden.Id);
    }

    [Fact]
    public async Task Offers_are_returned_in_sort_order()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();

        var last = await context.CreateOfferAsync(currency.Id, [product.Id]);
        var first = await context.CreateOfferAsync(currency.Id, [product.Id]);

        await context.Offers.Where(o => o.Id == last.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.SortOrder, 90));
        await context.Offers.Where(o => o.Id == first.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.SortOrder, -90));

        var userId = await TestData.CreateUserAsync(context);
        var listed = (await Offers(context).GetForUserAsync(userId)).Offers.Select(o => o.OfferId).ToList();

        Assert.True(listed.IndexOf(first.Id) < listed.IndexOf(last.Id));
    }

    // ------------------------------------------------------------- fixtures

    private static OfferService Offers(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        Guid? langId = null) =>
        new(context, new StubLanguageService(langId ?? LanguageIds.English));

    private static Task Credit(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        Guid userId,
        Guid currencyId,
        long amount) =>
        new WalletService(context).ApplyAsync(new WalletMutation
        {
            UserId = userId,
            CurrencyId = currencyId,
            Delta = amount,
            TransactionType = CurrencyTransactionType.AdminGrant,
            SourceType = LedgerSourceType.Admin
        });
}
