using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Commerce;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class OfferAdminServiceTests
{
    private readonly SqlServerFixture _fixture;

    public OfferAdminServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_offer_spells_out_what_each_of_its_products_hands_over()
    {
        // Counting grants is not enough to review a shop entry: a bundle whose second product grants
        // nothing looks identical to a working one until the references are on screen.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var kind = await context.CreateProductKindAsync("Cosmetic");

        var hat = await context.CreateProductAsync(
            [new GrantSpecification("cosmetic_hat"), new GrantSpecification("cosmetic_hat_glow", 2)],
            productKindId: kind.Id);

        var empty = await context.CreateProductAsync([], productKindId: kind.Id);

        var offer = await context.CreateOfferAsync(currency.Id, [hat.Id, empty.Id], price: 100);

        var read = await Admin(context).GetAsync(offer.Id);

        Assert.True(read.Succeeded, string.Join("; ", read.Errors));

        var hatDto = read.Value!.Products.Single(p => p.ProductId == hat.Id);
        Assert.Equal(2, hatDto.GrantCount);
        Assert.Equal(2, hatDto.Grants.Count);
        Assert.All(hatDto.Grants, g => Assert.Equal("COSMETIC", g.Kind));
        Assert.Equal(2, hatDto.Grants.Single(g => g.Reference == "cosmetic_hat_glow").Quantity);
        Assert.Equal("Test Product", hatDto.Name);

        // The product that hands over nothing is visible as such rather than as a bare count.
        var emptyDto = read.Value.Products.Single(p => p.ProductId == empty.Id);
        Assert.Equal(0, emptyDto.GrantCount);
        Assert.Empty(emptyDto.Grants);
    }

    [Fact]
    public async Task An_offer_reads_back_in_the_callers_language()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100);

        await context.OfferTranslations
            .Where(t => t.OfferId == offer.Id && t.LangId == LanguageIds.English)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Name, "Starter bundle"));

        await context.OfferTranslations
            .Where(t => t.OfferId == offer.Id && t.LangId == LanguageIds.Arabic)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Name, "حزمة البداية"));

        await using var english = _fixture.CreateContext();
        await using var arabic = _fixture.CreateContext();

        Assert.Equal("Starter bundle", (await Admin(english).GetAsync(offer.Id)).Value!.Name);
        Assert.Equal("حزمة البداية", (await Admin(arabic, LanguageIds.Arabic).GetAsync(offer.Id)).Value!.Name);
    }

    [Fact]
    public async Task A_language_with_no_translation_falls_back_to_english()
    {
        // The stated rule: the caller's language, and English when they have none of their own.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync();
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100);

        await context.OfferTranslations
            .Where(t => t.OfferId == offer.Id && t.LangId == LanguageIds.English)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Name, "English only"));

        // Drop the Arabic row, standing in for a language the offer was never translated into.
        await context.OfferTranslations
            .Where(t => t.OfferId == offer.Id && t.LangId == LanguageIds.Arabic)
            .ExecuteDeleteAsync();

        await using var check = _fixture.CreateContext();
        var read = await Admin(check, LanguageIds.Arabic).GetAsync(offer.Id);

        Assert.Equal("English only", read.Value!.Name);
    }

    [Fact]
    public async Task The_listing_carries_the_same_grant_detail_as_the_single_read()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var product = await context.CreateProductAsync([new GrantSpecification("cosmetic_listed")]);
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100);

        var listed = (await Admin(context).GetAllAsync()).Single(o => o.OfferId == offer.Id);

        Assert.Equal(
            "cosmetic_listed",
            listed.Products.Single(p => p.ProductId == product.Id).Grants.Single().Reference);
    }

    private static OfferAdminService Admin(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        Guid? langId = null) =>
        new(context, new StubLanguageService(langId ?? LanguageIds.English));
}
