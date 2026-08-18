using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;
using Share7.Domain.Constants;
using Share7.Infrastructure.Commerce;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class ProductAdminServiceTests
{
    private readonly SqlServerFixture _fixture;

    public ProductAdminServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_product_carries_its_kind_art_and_text_in_every_language()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync("Cosmetic");

        var result = await new ProductAdminService(context).CreateAsync(new CreateProductRequest
        {
            Key = Key(),
            ImageUrl = "https://cdn.example.com/shop/astronaut.png",
            ProductKindId = kind.Id,
            Translations =
            [
                new CommerceTranslationRequest
                {
                    LangId = LanguageIds.English, Name = "Astronaut skin", Description = "Gold visor."
                },
                new CommerceTranslationRequest
                {
                    LangId = LanguageIds.Arabic, Name = "زي رائد الفضاء", Description = "بقناع ذهبي."
                }
            ]
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal("https://cdn.example.com/shop/astronaut.png", result.Value!.ImageUrl);
        Assert.Equal(kind.Id, result.Value.ProductKindId);
        Assert.True(result.Value.Active);
        Assert.Equal(0, result.Value.OwnerCount);

        var english = result.Value.Translations.Single(t => t.LangId == LanguageIds.English);
        var arabic = result.Value.Translations.Single(t => t.LangId == LanguageIds.Arabic);

        Assert.Equal("Astronaut skin", english.Name);
        Assert.Equal("en", english.LangCode);
        Assert.Equal("زي رائد الفضاء", arabic.Name);
        Assert.Equal("بقناع ذهبي.", arabic.Description);

        // A new product hands over nothing until grants are added through their own endpoints.
        Assert.Empty(result.Value.Grants);
    }

    [Fact]
    public async Task A_product_missing_a_language_is_refused()
    {
        // The whole point of requiring every language: a shop entry with no Arabic text is one an
        // Arabic student cannot read, and there is no fallback rule to hide that behind.
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync();

        var result = await new ProductAdminService(context).CreateAsync(new CreateProductRequest
        {
            Key = Key(),
            ProductKindId = kind.Id,
            Translations = [new CommerceTranslationRequest { LangId = LanguageIds.English, Name = "English only" }]
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductInvalid.Code, result.Error!.Code);
        Assert.Equal(ServiceErrorKind.Validation, result.ErrorKind);
        Assert.Contains("ar", (IEnumerable<string>)result.Details!["missingLanguages"]!);
    }

    [Fact]
    public async Task A_blank_name_in_one_language_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync();

        var translations = await context.TranslationsAsync("Fine");
        translations[1].Name = "   ";

        var result = await new ProductAdminService(context).CreateAsync(new CreateProductRequest
        {
            Key = Key(),
            ProductKindId = kind.Id,
            Translations = translations
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductInvalid.Code, result.Error!.Code);
    }

    [Fact]
    public async Task The_same_language_twice_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync();

        var result = await new ProductAdminService(context).CreateAsync(new CreateProductRequest
        {
            Key = Key(),
            ProductKindId = kind.Id,
            Translations =
            [
                new CommerceTranslationRequest { LangId = LanguageIds.English, Name = "First" },
                new CommerceTranslationRequest { LangId = LanguageIds.English, Name = "Second" },
                new CommerceTranslationRequest { LangId = LanguageIds.Arabic, Name = "عربي" }
            ]
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductInvalid.Code, result.Error!.Code);
    }

    [Fact]
    public async Task An_update_replaces_the_text_in_every_language()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync();
        var service = new ProductAdminService(context);

        var created = await service.CreateAsync(await ProductAsync(context, kind.Id));
        Assert.True(created.Succeeded, string.Join("; ", created.Errors));

        var updated = await service.UpdateAsync(created.Value!.ProductId, new UpdateProductRequest
        {
            ProductKindId = kind.Id,
            Translations =
            [
                new CommerceTranslationRequest { LangId = LanguageIds.English, Name = "Renamed" },
                new CommerceTranslationRequest { LangId = LanguageIds.Arabic, Name = "أعيدت التسمية" }
            ]
        });

        Assert.True(updated.Succeeded, string.Join("; ", updated.Errors));
        Assert.Equal("Renamed", updated.Value!.Translations.Single(t => t.LangCode == "en").Name);
        Assert.Equal("أعيدت التسمية", updated.Value.Translations.Single(t => t.LangCode == "ar").Name);

        // Rewritten in place, not cleared and re-added — the key is (ProductId, LangId).
        await using var check = _fixture.CreateContext();
        Assert.Equal(2, await check.ProductTranslations.CountAsync(t => t.ProductId == created.Value.ProductId));
    }

    [Fact]
    public async Task Translations_go_with_a_deleted_product()
    {
        await using var context = _fixture.CreateContext();
        var product = await context.CreateProductAsync([new GrantSpecification("cosmetic_doomed")]);

        Assert.True((await new ProductAdminService(context).DeleteAsync(product.Id)).Succeeded);

        await using var check = _fixture.CreateContext();
        Assert.False(await check.Products.AnyAsync(p => p.Id == product.Id));
        Assert.False(await check.ProductGrants.AnyAsync(g => g.ProductId == product.Id));
        Assert.False(await check.ProductTranslations.AnyAsync(t => t.ProductId == product.Id));
    }

    [Fact]
    public async Task The_kind_reaches_the_client_normalised()
    {
        // It is admin-authored text now rather than an enum member, so this is the only thing
        // keeping "Content Pack" and "content-pack" from becoming two tokens Unity cannot match.
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync("Content Pack");

        var created = await new ProductAdminService(context).CreateAsync(await ProductAsync(context, kind.Id));

        Assert.True(created.Succeeded, string.Join("; ", created.Errors));
        Assert.Equal("Content Pack", created.Value!.KindName);
        Assert.Equal("CONTENT_PACK", created.Value.Kind);
    }

    [Fact]
    public async Task A_product_cannot_be_created_with_a_kind_that_does_not_exist()
    {
        await using var context = _fixture.CreateContext();

        var result = await new ProductAdminService(context)
            .CreateAsync(await ProductAsync(context, Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductKindNotFound.Code, result.Error!.Code);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task A_duplicate_key_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync();
        var service = new ProductAdminService(context);
        var key = Key();

        Assert.True((await service.CreateAsync(await ProductAsync(context, kind.Id, key))).Succeeded);

        var second = await service.CreateAsync(await ProductAsync(context, kind.Id, key));

        Assert.False(second.Succeeded);
        Assert.Equal(ApiErrors.ProductKeyTaken.Code, second.Error!.Code);
        Assert.Equal(ServiceErrorKind.Conflict, second.ErrorKind);
    }

    [Fact]
    public async Task A_product_can_be_re_categorised_after_it_has_sold()
    {
        // Deliberately allowed where editing the grant set is not. Kind changes how the client
        // *reads* the references; it does not change which references the owner receives, so a
        // miscategorised product can still be fixed once someone owns it.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var cosmetic = await context.CreateProductKindAsync("Cosmetic");
        var pack = await context.CreateProductKindAsync("Content Pack");
        var service = new ProductAdminService(context);

        var created = await service.CreateAsync(await ProductAsync(context, cosmetic.Id));
        await new EntitlementService(context)
            .GrantAsync(userId, created.Value!.ProductId, EntitlementSource.Purchase);

        var updated = await service.UpdateAsync(created.Value.ProductId, new UpdateProductRequest
        {
            ProductKindId = pack.Id,
            Translations = await context.TranslationsAsync("Recategorised")
        });

        Assert.True(updated.Succeeded, string.Join("; ", updated.Errors));
        Assert.Equal("CONTENT_PACK", updated.Value!.Kind);
        Assert.Equal(1, updated.Value.OwnerCount);
    }

    [Fact]
    public async Task An_owned_product_can_still_be_retitled_and_retired()
    {
        // Pulling a product from the shop has to stay possible.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var kind = await context.CreateProductKindAsync();
        var service = new ProductAdminService(context);

        var created = await service.CreateAsync(await ProductAsync(context, kind.Id));
        await new EntitlementService(context)
            .GrantAsync(userId, created.Value!.ProductId, EntitlementSource.Purchase);

        var updated = await service.UpdateAsync(created.Value.ProductId, new UpdateProductRequest
        {
            ProductKindId = kind.Id,
            Active = false,
            Translations = await context.TranslationsAsync("Retired bundle")
        });

        Assert.True(updated.Succeeded, string.Join("; ", updated.Errors));
        Assert.False(updated.Value!.Active);
        Assert.Equal(1, updated.Value.OwnerCount);
    }

    [Fact]
    public async Task An_owned_product_cannot_be_deleted()
    {
        // The requirement the commerce contract is most explicit about: an entitlement resolves
        // through to the product, so deleting one would strand every account that bought it.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync();

        await new EntitlementService(context).GrantAsync(userId, product.Id, EntitlementSource.Purchase);

        var result = await new ProductAdminService(context).DeleteAsync(product.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductOwned.Code, result.Error!.Code);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);
        Assert.Equal(1, result.Details!["ownerCount"]);

        await using var check = _fixture.CreateContext();
        Assert.True(await check.Products.AnyAsync(p => p.Id == product.Id));
    }

    [Fact]
    public async Task Deleting_a_product_that_is_already_gone_succeeds()
    {
        await using var context = _fixture.CreateContext();

        Assert.True((await new ProductAdminService(context).DeleteAsync(Guid.NewGuid())).Succeeded);
    }

    [Fact]
    public async Task Updating_a_product_that_does_not_exist_reports_not_found()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync();

        var result = await new ProductAdminService(context).UpdateAsync(
            Guid.NewGuid(),
            new UpdateProductRequest
            {
                ProductKindId = kind.Id,
                Translations = await context.TranslationsAsync("Nothing")
            });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductNotFound.Code, result.Error!.Code);
    }

    [Fact]
    public async Task A_product_reads_back_with_its_grants_and_owner_count()
    {
        await using var context = _fixture.CreateContext();
        var first = await TestData.CreateUserAsync(context);
        var second = await TestData.CreateUserAsync(context);
        var kind = await context.CreateProductKindAsync("Cosmetic");

        var product = await context.CreateProductAsync(
            [new GrantSpecification("cosmetic_a"), new GrantSpecification("cosmetic_b", 3)],
            productKindId: kind.Id);

        var entitlements = new EntitlementService(context);
        await entitlements.GrantAsync(first, product.Id, EntitlementSource.Purchase);
        await entitlements.GrantAsync(second, product.Id, EntitlementSource.AdminGrant);

        await using var check = _fixture.CreateContext();
        var read = await new ProductAdminService(check).GetAsync(product.Id);

        Assert.True(read.Succeeded, string.Join("; ", read.Errors));
        Assert.Equal(2, read.Value!.OwnerCount);
        Assert.Equal(2, read.Value.Grants.Count);
        Assert.Equal(2, read.Value.Translations.Count);

        // Every grant reports the product's kind, which is what the contract's grant shape expects.
        Assert.All(read.Value.Grants, grant => Assert.Equal("COSMETIC", grant.Kind));
        Assert.Equal(3, read.Value.Grants.Single(g => g.Reference == "cosmetic_b").Quantity);
    }

    [Fact]
    public async Task Listing_includes_retired_products()
    {
        await using var context = _fixture.CreateContext();
        var retired = await context.CreateProductAsync(active: false);

        var listed = await new ProductAdminService(context).GetAllAsync();

        Assert.Contains(listed, p => p.ProductId == retired.Id && !p.Active);
    }

    private static string Key() => $"p_{Guid.NewGuid():N}"[..20];

    private static async Task<CreateProductRequest> ProductAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        Guid productKindId,
        string? key = null) => new()
    {
        Key = key ?? Key(),
        ProductKindId = productKindId,
        Translations = await context.TranslationsAsync("Test Product")
    };
}
