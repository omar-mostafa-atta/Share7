using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Commerce;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class ProductGrantAdminServiceTests
{
    private readonly SqlServerFixture _fixture;

    public ProductGrantAdminServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_product_can_be_given_several_things_to_hand_over()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync("Cosmetic");
        var product = await context.CreateProductAsync([], productKindId: kind.Id);
        var service = new ProductGrantAdminService(context);

        Assert.True((await service.CreateAsync(Grant(product.Id, "cosmetic_astronaut"))).Succeeded);
        Assert.True((await service.CreateAsync(Grant(product.Id, "cosmetic_trail", quantity: 2))).Succeeded);

        await using var check = _fixture.CreateContext();
        var grants = await new ProductGrantAdminService(check).GetAllAsync(product.Id);

        Assert.Equal(2, grants.Count);
        Assert.All(grants, grant => Assert.Equal("COSMETIC", grant.Kind));
        Assert.Equal(2, grants.Single(g => g.Reference == "cosmetic_trail").Quantity);
    }

    [Fact]
    public async Task The_same_reference_twice_in_one_product_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var product = await context.CreateProductAsync([]);
        var service = new ProductGrantAdminService(context);

        Assert.True((await service.CreateAsync(Grant(product.Id, "cosmetic_dup"))).Succeeded);

        var second = await service.CreateAsync(Grant(product.Id, "cosmetic_dup"));

        Assert.False(second.Succeeded);
        Assert.Equal(ApiErrors.ProductGrantReferenceTaken.Code, second.Error!.Code);
        Assert.Equal(ServiceErrorKind.Conflict, second.ErrorKind);
    }

    [Fact]
    public async Task Two_products_may_grant_the_same_reference()
    {
        // Uniqueness is per product. Two shop entries handing over the same cosmetic is ordinary
        // catalogue design — a starter bundle and the standalone skin, say.
        await using var context = _fixture.CreateContext();
        var first = await context.CreateProductAsync([]);
        var second = await context.CreateProductAsync([]);
        var service = new ProductGrantAdminService(context);

        Assert.True((await service.CreateAsync(Grant(first.Id, "cosmetic_shared"))).Succeeded);
        Assert.True((await service.CreateAsync(Grant(second.Id, "cosmetic_shared"))).Succeeded);
    }

    [Fact]
    public async Task Concurrent_adds_of_the_same_reference_produce_one_row()
    {
        // The check-then-insert would let both through; the unique index is what actually decides.
        await using var setup = _fixture.CreateContext();
        var product = await setup.CreateProductAsync([]);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await new ProductGrantAdminService(context).CreateAsync(Grant(product.Id, "cosmetic_raced"));
        }));

        Assert.Single(attempts.Where(a => a.Succeeded));
        Assert.All(attempts.Where(a => !a.Succeeded),
            attempt => Assert.Equal(ApiErrors.ProductGrantReferenceTaken.Code, attempt.Error!.Code));

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.ProductGrants.CountAsync(g => g.ProductId == product.Id));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public async Task A_blank_reference_is_refused(string reference)
    {
        // The reference is the entire contract with the client — there is no catalogue here to
        // check it against, so an empty one would produce a product granting nothing findable.
        await using var context = _fixture.CreateContext();
        var product = await context.CreateProductAsync([]);

        var result = await new ProductGrantAdminService(context).CreateAsync(Grant(product.Id, reference));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductGrantInvalid.Code, result.Error!.Code);
    }

    [Fact]
    public async Task A_quantity_below_one_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var product = await context.CreateProductAsync([]);

        var result = await new ProductGrantAdminService(context)
            .CreateAsync(Grant(product.Id, "cosmetic_zero", quantity: 0));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductGrantInvalid.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Adding_a_grant_to_a_product_that_does_not_exist_reports_not_found()
    {
        await using var context = _fixture.CreateContext();

        var result = await new ProductGrantAdminService(context)
            .CreateAsync(Grant(Guid.NewGuid(), "cosmetic_orphan"));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductNotFound.Code, result.Error!.Code);
    }

    [Fact]
    public async Task A_grant_can_be_edited_and_deleted_while_nobody_owns_the_product()
    {
        await using var context = _fixture.CreateContext();
        var product = await context.CreateProductAsync([new GrantSpecification("cosmetic_old")]);
        var grantId = product.Grants.Single().Id;
        var service = new ProductGrantAdminService(context);

        var updated = await service.UpdateAsync(grantId, new UpdateProductGrantRequest
        {
            Reference = "cosmetic_new",
            Quantity = 5
        });

        Assert.True(updated.Succeeded, string.Join("; ", updated.Errors));
        Assert.Equal("cosmetic_new", updated.Value!.Reference);
        Assert.Equal(5, updated.Value.Quantity);

        Assert.True((await service.DeleteAsync(grantId)).Succeeded);

        await using var check = _fixture.CreateContext();
        Assert.Empty(await check.ProductGrants.Where(g => g.ProductId == product.Id).ToListAsync());
    }

    [Fact]
    public async Task Grants_cannot_be_added_once_an_account_owns_the_product()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync();

        await new EntitlementService(context).GrantAsync(userId, product.Id, EntitlementSource.Purchase);

        var result = await new ProductGrantAdminService(context)
            .CreateAsync(Grant(product.Id, "cosmetic_sneaked_in"));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductGrantsLocked.Code, result.Error!.Code);
        Assert.Equal(1, result.Details!["ownerCount"]);

        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.ProductGrants.CountAsync(g => g.ProductId == product.Id));
    }

    [Fact]
    public async Task Grants_cannot_be_edited_once_an_account_owns_the_product()
    {
        // Editing one would silently change what that account owns, because the entitlement reads
        // through to these rows rather than snapshotting them.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync([new GrantSpecification("cosmetic_owned")]);
        var grantId = product.Grants.Single().Id;

        await new EntitlementService(context).GrantAsync(userId, product.Id, EntitlementSource.Purchase);

        var result = await new ProductGrantAdminService(context).UpdateAsync(grantId, new UpdateProductGrantRequest
        {
            Reference = "cosmetic_something_else"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductGrantsLocked.Code, result.Error!.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal("cosmetic_owned", (await check.ProductGrants.SingleAsync(g => g.Id == grantId)).Reference);
    }

    [Fact]
    public async Task Grants_cannot_be_deleted_once_an_account_owns_the_product()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync([new GrantSpecification("cosmetic_kept")]);
        var grantId = product.Grants.Single().Id;

        await new EntitlementService(context).GrantAsync(userId, product.Id, EntitlementSource.Purchase);

        var result = await new ProductGrantAdminService(context).DeleteAsync(grantId);

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductGrantsLocked.Code, result.Error!.Code);

        await using var check = _fixture.CreateContext();
        Assert.True(await check.ProductGrants.AnyAsync(g => g.Id == grantId));
    }

    [Fact]
    public async Task Deleting_a_grant_that_is_already_gone_succeeds()
    {
        await using var context = _fixture.CreateContext();

        var result = await new ProductGrantAdminService(context).DeleteAsync(Guid.NewGuid());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task A_grant_reports_the_kind_of_the_product_it_belongs_to()
    {
        await using var context = _fixture.CreateContext();
        var kind = await context.CreateProductKindAsync("Content Pack");
        var product = await context.CreateProductAsync([new GrantSpecification("skins_pack_01")], productKindId: kind.Id);

        var grant = await new ProductGrantAdminService(context).GetAsync(product.Grants.Single().Id);

        Assert.True(grant.Succeeded, string.Join("; ", grant.Errors));
        Assert.Equal("CONTENT_PACK", grant.Value!.Kind);
        Assert.Equal(product.Id, grant.Value.ProductId);
    }

    private static CreateProductGrantRequest Grant(Guid productId, string reference, int quantity = 1) => new()
    {
        ProductId = productId,
        Reference = reference,
        Quantity = quantity
    };
}
