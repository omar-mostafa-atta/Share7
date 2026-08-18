using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Commerce;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class EntitlementServiceTests
{
    private readonly SqlServerFixture _fixture;

    public EntitlementServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Granting_a_product_records_ownership()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync();

        var result = await new EntitlementService(context)
            .GrantAsync(userId, product.Id, EntitlementSource.AdminGrant, "admin-1");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.False(result.Value!.AlreadyOwned);
        Assert.Equal(product.Id, result.Value.Entitlement.ProductId);
        Assert.Equal("ADMIN_GRANT", result.Value.Entitlement.Source);

        await using var check = _fixture.CreateContext();
        var stored = Assert.Single(await check.EntitlementsOfAsync(userId));
        Assert.Equal("admin-1", stored.SourceId);
    }

    [Fact]
    public async Task Granted_at_is_reported_as_utc_with_a_zone()
    {
        // SQL Server hands datetime2 back with Kind = Unspecified, which serializes without a Z and
        // leaves the client guessing. The contract shows a Z, so the kind is set explicitly.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync();
        var service = new EntitlementService(context);

        await service.GrantAsync(userId, product.Id, EntitlementSource.AdminGrant);

        await using var read = _fixture.CreateContext();
        var entitlement = Assert.Single(await new EntitlementService(read).GetForUserAsync(userId));

        Assert.Equal(DateTimeKind.Utc, entitlement.GrantedAtUtc.Kind);
        Assert.EndsWith("Z", System.Text.Json.JsonSerializer.Serialize(entitlement.GrantedAtUtc).Trim('"'));
    }

    [Fact]
    public async Task Granting_the_same_product_twice_changes_nothing()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync();
        var service = new EntitlementService(context);

        var first = await service.GrantAsync(userId, product.Id, EntitlementSource.Purchase, "txn-1");
        var second = await service.GrantAsync(userId, product.Id, EntitlementSource.AdminGrant, "admin-1");

        Assert.False(first.Value!.AlreadyOwned);
        Assert.True(second.Value!.AlreadyOwned);

        // The original wins — a second grant does not rewrite how the account came to own it.
        Assert.Equal(first.Value.Entitlement.EntitlementId, second.Value.Entitlement.EntitlementId);
        Assert.Equal("PURCHASE", second.Value.Entitlement.Source);

        await using var check = _fixture.CreateContext();
        Assert.Single(await check.EntitlementsOfAsync(userId));
    }

    [Fact]
    public async Task Concurrent_grants_create_exactly_one_entitlement()
    {
        await using var setup = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(setup);
        var product = await setup.CreateProductAsync();

        // Every one of these reads "not owned" before any of them inserts. The unique index is what
        // decides, which is the same guarantee a retried purchase will rely on.
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await new EntitlementService(context)
                .GrantAsync(userId, product.Id, EntitlementSource.Purchase, "txn-race");
        });

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.Succeeded, string.Join("; ", r.Errors)));
        Assert.Equal(1, results.Count(r => !r.Value!.AlreadyOwned));

        var ids = results.Select(r => r.Value!.Entitlement.EntitlementId).Distinct().ToList();
        Assert.Single(ids);

        await using var check = _fixture.CreateContext();
        Assert.Equal(ids[0], Assert.Single(await check.EntitlementsOfAsync(userId)).Id);
    }

    [Fact]
    public async Task An_entitlement_survives_its_product_being_retired()
    {
        // The requirement the commerce contract is most explicit about: delisting must not revoke
        // ownership, and what was owned must stay resolvable.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var product = await context.CreateProductAsync(
        [
            new GrantSpecification("cosmetic_astronaut"),
            new GrantSpecification("skins_pack_01")
        ]);

        var granted = await new EntitlementService(context)
            .GrantAsync(userId, product.Id, EntitlementSource.Purchase, "txn-9");

        Assert.True(granted.Succeeded, string.Join("; ", granted.Errors));

        // Pull it from the shop.
        await context.Products
            .Where(p => p.Id == product.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Active, false));

        await using var check = _fixture.CreateContext();

        // Still owned, still listed.
        var listed = Assert.Single(await new EntitlementService(check).GetForUserAsync(userId));
        Assert.Equal(product.Id, listed.ProductId);

        // And Entitlement → Product → ProductGrant still walks.
        var grants = await check.ResolveGrantsAsync(listed.EntitlementId);
        Assert.Equal(2, grants.Count);
        Assert.Contains(grants, g => g.Reference == "cosmetic_astronaut");
        Assert.Contains(grants, g => g.Reference == "skins_pack_01");

        // The kind survives the retirement too — it hangs off the product, which is still there.
        var productKind = await check.Products
            .AsNoTracking()
            .Where(p => p.Id == product.Id)
            .Select(p => p.ProductKindId)
            .SingleAsync();

        Assert.NotEqual(Guid.Empty, productKind);
    }

    [Fact]
    public async Task A_retired_product_cannot_be_newly_granted()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync(active: false);

        var result = await new EntitlementService(context)
            .GrantAsync(userId, product.Id, EntitlementSource.AdminGrant);

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductInactive.Code, result.Error!.Code);
        Assert.Equal("commerce.product.inactive", result.Error.MessageKey);

        await using var check = _fixture.CreateContext();
        Assert.Empty(await check.EntitlementsOfAsync(userId));
    }

    [Fact]
    public async Task Granting_a_product_that_does_not_exist_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await new EntitlementService(context)
            .GrantAsync(userId, Guid.NewGuid(), EntitlementSource.AdminGrant);

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductNotFound.Code, result.Error!.Code);
        Assert.Equal(ServiceErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task A_product_cannot_be_deleted_while_anyone_owns_it()
    {
        // Restrict rather than cascade: deleting the product would strand the entitlement, which
        // resolves what it owns by reading through to that product's grants.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync();

        await new EntitlementService(context).GrantAsync(userId, product.Id, EntitlementSource.Purchase);

        await using var attempt = _fixture.CreateContext();

        // ExecuteDelete bypasses the change tracker, so the database's refusal arrives as a raw
        // SqlException rather than wrapped in DbUpdateException.
        var exception = await Assert.ThrowsAsync<SqlException>(() =>
            attempt.Products.Where(p => p.Id == product.Id).ExecuteDeleteAsync());

        Assert.Contains("FK_Entitlements_Products_ProductId", exception.Message);

        await using var check = _fixture.CreateContext();
        Assert.True(await check.Products.AnyAsync(p => p.Id == product.Id));
    }

    [Fact]
    public async Task Entitlements_are_scoped_to_the_account()
    {
        await using var context = _fixture.CreateContext();
        var owner = await TestData.CreateUserAsync(context);
        var bystander = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync();
        var service = new EntitlementService(context);

        await service.GrantAsync(owner, product.Id, EntitlementSource.Purchase);

        Assert.Single(await service.GetForUserAsync(owner));
        Assert.Empty(await service.GetForUserAsync(bystander));
    }

    [Fact]
    public async Task A_grant_joins_the_callers_transaction()
    {
        // What phase 07 depends on: the purchase deducts currency and grants entitlements as one
        // unit, so this must not commit on its own.
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var product = await context.CreateProductAsync();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var result = await new EntitlementService(context)
                .GrantAsync(userId, product.Id, EntitlementSource.Purchase, "txn-rollback");

            Assert.True(result.Succeeded, string.Join("; ", result.Errors));

            await transaction.RollbackAsync();
        }

        await using var check = _fixture.CreateContext();
        Assert.Empty(await check.EntitlementsOfAsync(userId));
    }

    [Fact]
    public async Task Ownership_is_listed_newest_first()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var service = new EntitlementService(context);

        var first = await context.CreateProductAsync();
        await service.GrantAsync(userId, first.Id, EntitlementSource.Purchase);

        var second = await context.CreateProductAsync();
        await service.GrantAsync(userId, second.Id, EntitlementSource.AdminGrant);

        var listed = await service.GetForUserAsync(userId);

        Assert.Equal(2, listed.Count);
        Assert.Equal(second.Id, listed[0].ProductId);
        Assert.Equal(first.Id, listed[1].ProductId);
    }
}
