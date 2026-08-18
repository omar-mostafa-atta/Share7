using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Commerce;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class ProductKindAdminServiceTests
{
    private readonly SqlServerFixture _fixture;

    public ProductKindAdminServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("Content Pack")]
    [InlineData("content-pack")]
    [InlineData("ContentPack")]
    [InlineData("content_pack")]
    [InlineData("  CONTENT_PACK  ")]
    public void Every_spelling_of_a_kind_normalises_to_one_token(string spelling)
    {
        // Kind stopped being an enum, so this replaces what the compiler used to guarantee: the
        // vocabulary the client matches on cannot vary with how an admin happened to type it.
        Assert.Equal("CONTENT_PACK", ProductKindName.ToWire(spelling));
    }

    [Fact]
    public async Task A_kind_reports_the_token_the_client_will_see()
    {
        await using var context = _fixture.CreateContext();
        var name = $"Trail Effect {Guid.NewGuid():N}"[..24];

        var result = await new ProductKindAdminService(context).CreateAsync(new CreateProductKindRequest
        {
            Name = name,
            Translations = await context.TranslationsAsync(name)
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(ProductKindName.ToWire(result.Value!.Name), result.Value.Kind);
        Assert.StartsWith("TRAIL_EFFECT_", result.Value.Kind);
    }

    [Fact]
    public async Task A_kind_carries_an_admin_label_per_language_that_the_client_never_sees()
    {
        // The token and the label are deliberately separate: COSMETIC has to mean the same thing to
        // an Arabic client as to an English one, so only the human label is translated.
        await using var context = _fixture.CreateContext();
        var name = $"Skin{Guid.NewGuid():N}"[..12];

        var result = await new ProductKindAdminService(context).CreateAsync(new CreateProductKindRequest
        {
            Name = name,
            Translations =
            [
                new CommerceTranslationRequest { LangId = LanguageIds.English, Name = "Skins", Description = "Character art." },
                new CommerceTranslationRequest { LangId = LanguageIds.Arabic, Name = "أزياء", Description = "مظهر الشخصية." }
            ]
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal("Skins", result.Value!.Translations.Single(t => t.LangCode == "en").Name);
        Assert.Equal("أزياء", result.Value.Translations.Single(t => t.LangCode == "ar").Name);

        // The token still comes from the untranslated machine name, not from either label.
        Assert.Equal(ProductKindName.ToWire(name), result.Value.Kind);
    }

    [Fact]
    public async Task A_kind_missing_a_language_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var name = $"Half{Guid.NewGuid():N}"[..12];

        var result = await new ProductKindAdminService(context).CreateAsync(new CreateProductKindRequest
        {
            Name = name,
            Translations = [new CommerceTranslationRequest { LangId = LanguageIds.English, Name = "English only" }]
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductKindInvalid.Code, result.Error!.Code);
        Assert.Contains("ar", (IEnumerable<string>)result.Details!["missingLanguages"]!);
    }

    [Fact]
    public async Task Two_kinds_that_normalise_the_same_cannot_both_exist()
    {
        // The unique index only catches identical text. Two rows producing one token would be
        // indistinguishable to the client, which is the thing that actually matters.
        await using var context = _fixture.CreateContext();
        var service = new ProductKindAdminService(context);
        var name = $"Pack{Guid.NewGuid():N}"[..12];

        Assert.True((await service.CreateAsync(new CreateProductKindRequest
        {
            Name = name,
            Translations = await context.TranslationsAsync(name)
        })).Succeeded);

        var second = await service.CreateAsync(new CreateProductKindRequest
        {
            Name = name.ToLowerInvariant(),
            Translations = await context.TranslationsAsync(name)
        });

        Assert.False(second.Succeeded);
        Assert.Equal(ApiErrors.ProductKindNameTaken.Code, second.Error!.Code);
        Assert.Equal(ServiceErrorKind.Conflict, second.ErrorKind);
    }

    [Fact]
    public async Task A_blank_machine_name_is_refused()
    {
        await using var context = _fixture.CreateContext();

        var result = await new ProductKindAdminService(context).CreateAsync(new CreateProductKindRequest
        {
            Name = "   ",
            Translations = await context.TranslationsAsync("Fine")
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductKindInvalid.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Renaming_a_kind_changes_what_every_product_of_that_kind_reports()
    {
        // Worth pinning down because it is a contract change dressed as an edit: the products are
        // untouched, and Unity starts receiving a different token for all of them.
        //
        // Named uniquely, unlike the read-only kinds elsewhere — this test mutates the row, and
        // every test class shares one database.
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync($"Before{Guid.NewGuid():N}"[..16]);
        var product = await context.CreateProductAsync(productKindId: kind.Id);

        var newName = $"After{Guid.NewGuid():N}"[..16];

        var renamed = await new ProductKindAdminService(context).UpdateAsync(kind.Id, new UpdateProductKindRequest
        {
            Name = newName,
            Translations = await context.TranslationsAsync(newName)
        });

        Assert.True(renamed.Succeeded, string.Join("; ", renamed.Errors));
        Assert.Equal(ProductKindName.ToWire(newName), renamed.Value!.Kind);

        await using var check = _fixture.CreateContext();
        var read = await new ProductAdminService(check).GetAsync(product.Id);

        Assert.Equal(ProductKindName.ToWire(newName), read.Value!.Kind);
        Assert.All(read.Value.Grants, grant => Assert.Equal(ProductKindName.ToWire(newName), grant.Kind));
    }

    [Fact]
    public async Task A_kind_a_product_still_uses_cannot_be_deleted()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync();
        await context.CreateProductAsync(productKindId: kind.Id);

        var result = await new ProductKindAdminService(context).DeleteAsync(kind.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductKindInUse.Code, result.Error!.Code);
        Assert.Equal(1, result.Details!["productCount"]);

        await using var check = _fixture.CreateContext();
        Assert.True(await check.ProductKinds.AnyAsync(k => k.Id == kind.Id));
    }

    [Fact]
    public async Task An_unused_kind_deletes_and_takes_its_labels_with_it()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync();

        var result = await new ProductKindAdminService(context).DeleteAsync(kind.Id);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        await using var check = _fixture.CreateContext();
        Assert.False(await check.ProductKinds.AnyAsync(k => k.Id == kind.Id));
        Assert.False(await check.ProductKindTranslations.AnyAsync(t => t.ProductKindId == kind.Id));
    }

    [Fact]
    public async Task Deleting_a_kind_that_is_already_gone_succeeds()
    {
        await using var context = _fixture.CreateContext();

        var result = await new ProductKindAdminService(context).DeleteAsync(Guid.NewGuid());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task Product_count_is_reported_so_a_refused_delete_is_visible_first()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync();
        await context.CreateProductAsync(productKindId: kind.Id);
        await context.CreateProductAsync(productKindId: kind.Id);

        var listed = (await new ProductKindAdminService(context).GetAllAsync())
            .Single(k => k.ProductKindId == kind.Id);

        Assert.Equal(2, listed.ProductCount);
    }

    [Fact]
    public async Task Renaming_a_kind_to_its_own_spelling_variant_is_allowed()
    {
        // The collision check has to exclude the row being edited, or fixing the capitalisation of
        // a kind would be impossible.
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync("content pack " + Guid.NewGuid().ToString("N")[..6]);

        var result = await new ProductKindAdminService(context).UpdateAsync(kind.Id, new UpdateProductKindRequest
        {
            Name = kind.Name.ToUpperInvariant(),
            Translations = await context.TranslationsAsync(kind.Name)
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task Editing_one_language_leaves_the_other_row_in_place()
    {
        // The composite key is (ProductKindId, LangId), so an untouched language has to stay the
        // same row rather than being deleted and re-inserted.
        await using var context = _fixture.CreateContext();
        var name = $"Edit{Guid.NewGuid():N}"[..12];
        var kind = await context.CreateProductKindAsync(name);
        var service = new ProductKindAdminService(context);

        var updated = await service.UpdateAsync(kind.Id, new UpdateProductKindRequest
        {
            Name = name,
            Translations =
            [
                new CommerceTranslationRequest { LangId = LanguageIds.English, Name = "Changed" },
                new CommerceTranslationRequest { LangId = LanguageIds.Arabic, Name = name }
            ]
        });

        Assert.True(updated.Succeeded, string.Join("; ", updated.Errors));
        Assert.Equal("Changed", updated.Value!.Translations.Single(t => t.LangCode == "en").Name);

        await using var check = _fixture.CreateContext();
        Assert.Equal(2, await check.ProductKindTranslations.CountAsync(t => t.ProductKindId == kind.Id));
    }
}
