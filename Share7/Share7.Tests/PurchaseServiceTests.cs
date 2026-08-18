using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Models;
using Share7.Domain.Commerce;
using Share7.Domain.Economy;
using Share7.Infrastructure.Commerce;
using Share7.Infrastructure.Economy;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The transactional heart of the shop. These are the tests the commerce contract singles out as
/// mattering most — a double charge or a charge without a grant is unrecoverable in a way a wrong
/// price is not.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PurchaseServiceTests
{
    private readonly SqlServerFixture _fixture;

    public PurchaseServiceTests(SqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_purchase_charges_once_grants_everything_and_records_the_transaction()
    {
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, productIds) = await ShopAsync(context, balance: 500, price: 100, products: 2);

        var result = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal("COMPLETED", result.Value!.State);
        Assert.NotNull(result.Value.TransactionId);
        Assert.False(result.Value.Replayed);
        Assert.Null(result.Value.FailureReasonKey);

        // Everything the offer sells, and what each hands over.
        Assert.Equal(2, result.Value.ProductIds.Count);
        Assert.Equal(2, result.Value.Products.Count);
        Assert.Equal(2, result.Value.Entitlements.Count);
        Assert.All(result.Value.Products, p => Assert.NotEmpty(p.Grants));

        // Absolute balance, not a delta.
        Assert.Equal(400, result.Value.Balances.Single(b => b.Currency == currency.Key).Amount);

        await using var check = _fixture.CreateContext();
        Assert.Equal(400, await check.BalanceOfAsync(userId, currency.Id));
        Assert.Equal(2, (await check.EntitlementsOfAsync(userId)).Count);

        var recorded = Assert.Single(await check.TransactionsOfAsync(userId));
        Assert.Equal(TransactionState.Completed, recorded.State);
        Assert.Equal(100, recorded.Price);
        Assert.Equal(offer.Id, recorded.OfferId);

        // The debit is on the ledger as a purchase, pointing at the offer.
        var debit = Assert.Single(await check.LedgerOfAsync(userId), e => e.Amount < 0);
        Assert.Equal(-100, debit.Amount);
        Assert.Equal(CurrencyTransactionType.Purchase, debit.TransactionType);
        Assert.Equal(offer.Id.ToString(), debit.SourceId);
    }

    [Fact]
    public async Task Too_few_coins_charges_nothing_grants_nothing_and_says_why()
    {
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(context, balance: 50, price: 100, products: 1);

        var result = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.InsufficientBalance.Code, result.Error!.Code);
        Assert.Equal(ServiceErrorKind.Conflict, result.ErrorKind);

        // A refusal is still a full answer: state, reason and authoritative balances.
        Assert.Equal("REFUSED", result.Value!.State);
        Assert.Equal("commerce.insufficient_balance", result.Value.FailureReasonKey);
        Assert.Empty(result.Value.Entitlements);
        Assert.Empty(result.Value.ProductIds);
        Assert.Equal(50, result.Value.Balances.Single(b => b.Currency == currency.Key).Amount);

        await using var check = _fixture.CreateContext();
        Assert.Equal(50, await check.BalanceOfAsync(userId, currency.Id));
        Assert.Empty(await check.EntitlementsOfAsync(userId));

        // The refusal is recorded — an offer nobody can afford should be visible, not silent.
        var recorded = Assert.Single(await check.TransactionsOfAsync(userId));
        Assert.Equal(TransactionState.Refused, recorded.State);
        Assert.Equal("commerce.insufficient_balance", recorded.FailureReasonKey);

        // And nothing reached the ledger.
        Assert.DoesNotContain(await check.LedgerOfAsync(userId), e => e.Amount < 0);
    }

    [Fact]
    public async Task Retrying_with_the_same_request_id_returns_the_original_and_charges_once()
    {
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(context, balance: 500, price: 100, products: 1);
        var service = Purchase(context);
        var request = Request(offer.Id);

        var first = await service.PurchaseAsync(userId, request);
        var second = await service.PurchaseAsync(userId, request);

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        Assert.True(second.Succeeded, string.Join("; ", second.Errors));

        Assert.Equal(first.Value!.TransactionId, second.Value!.TransactionId);
        Assert.False(first.Value.Replayed);
        Assert.True(second.Value.Replayed);

        await using var check = _fixture.CreateContext();
        Assert.Equal(400, await check.BalanceOfAsync(userId, currency.Id));
        Assert.Single(await check.TransactionsOfAsync(userId));
        Assert.Single(await check.EntitlementsOfAsync(userId));
    }

    [Fact]
    public async Task Topping_up_and_retrying_the_same_request_id_succeeds()
    {
        // Refusals are **not** replayed. Idempotency protects a charge, and a refusal made none —
        // so a student who sees "not enough coins", tops up and taps buy again must go through.
        // Replaying the stale "no" here was a real bug: with a fixed requestId (Swagger's default,
        // or any client that reuses one) the purchase could never succeed again.
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(context, balance: 50, price: 100, products: 1);
        var request = Request(offer.Id);

        var refused = await Purchase(context).PurchaseAsync(userId, request);

        Assert.False(refused.Succeeded);
        Assert.Equal(ApiErrors.InsufficientBalance.Code, refused.Error!.Code);
        Assert.False(refused.Value!.Replayed);

        await Credit(context, userId, currency.Id, 1000);

        var retried = await Purchase(context).PurchaseAsync(userId, request);

        Assert.True(retried.Succeeded, string.Join("; ", retried.Errors));
        Assert.Equal("COMPLETED", retried.Value!.State);
        Assert.False(retried.Value.Replayed);
        Assert.NotEqual(refused.Value.TransactionId, retried.Value.TransactionId);

        await using var check = _fixture.CreateContext();
        Assert.Equal(950, await check.BalanceOfAsync(userId, currency.Id));

        // Both attempts are on record — the refusal is history, not something to be overwritten.
        var recorded = await check.TransactionsOfAsync(userId);
        Assert.Equal(2, recorded.Count);
        Assert.Single(recorded, t => t.State == TransactionState.Refused);
        Assert.Single(recorded, t => t.State == TransactionState.Completed);
    }

    [Fact]
    public async Task Repeated_refusals_can_share_one_request_id()
    {
        // The unique index is filtered to completed rows precisely so this works — otherwise a
        // refused attempt would permanently burn its requestId.
        await using var context = _fixture.CreateContext();
        var (userId, _, offer, _) = await ShopAsync(context, balance: 10, price: 100, products: 1);
        var request = Request(offer.Id);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = await Purchase(context).PurchaseAsync(userId, request);
            Assert.Equal(ApiErrors.InsufficientBalance.Code, result.Error!.Code);
        }

        await using var check = _fixture.CreateContext();
        Assert.Equal(3, (await check.TransactionsOfAsync(userId)).Count);
    }

    [Fact]
    public async Task A_completed_purchase_still_replays_after_the_offer_expires()
    {
        // The half that must not change: once money has moved, the answer is fixed forever.
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(context, balance: 500, price: 100, products: 1);
        var request = Request(offer.Id);

        var first = await Purchase(context).PurchaseAsync(userId, request);
        Assert.True(first.Succeeded, string.Join("; ", first.Errors));

        await context.Offers
            .Where(o => o.Id == offer.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));

        var replayed = await Purchase(context).PurchaseAsync(userId, request);

        Assert.True(replayed.Succeeded, string.Join("; ", replayed.Errors));
        Assert.True(replayed.Value!.Replayed);
        Assert.Equal(first.Value!.TransactionId, replayed.Value.TransactionId);

        await using var check = _fixture.CreateContext();
        Assert.Equal(400, await check.BalanceOfAsync(userId, currency.Id));
    }

    [Fact]
    public async Task Concurrent_duplicates_of_one_request_id_charge_exactly_once()
    {
        // The read at the top of PurchaseAsync cannot see a row that has not committed yet, so this
        // is the unique index doing the work, not the check.
        await using var setup = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(setup, balance: 1000, price: 100, products: 1);
        var request = Request(offer.Id);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await Purchase(context).PurchaseAsync(userId, request);
        }));

        Assert.All(results, r => Assert.True(r.Succeeded, string.Join("; ", r.Errors)));
        Assert.Single(results.Select(r => r.Value!.TransactionId).Distinct());

        await using var check = _fixture.CreateContext();
        Assert.Equal(900, await check.BalanceOfAsync(userId, currency.Id));
        Assert.Single(await check.TransactionsOfAsync(userId));
        Assert.Single(await check.EntitlementsOfAsync(userId));
    }

    [Fact]
    public async Task A_double_tapped_buy_button_charges_once_even_with_different_request_ids()
    {
        // The nastiest race here, and the one idempotency does **not** cover: two genuinely
        // different requestIds for the same offer arriving together. Both read "not owned", both
        // debit, and the loser's grant silently no-ops against the unique (user, product) index —
        // so without the "granted nothing new" unwind the account pays twice for one item.
        await using var setup = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(setup, balance: 1000, price: 100, products: 1);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await Purchase(context).PurchaseAsync(userId, Request(offer.Id));
        }));

        Assert.Single(results.Where(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded),
            r => Assert.Equal(ApiErrors.AlreadyOwned.Code, r.Error!.Code));

        await using var check = _fixture.CreateContext();
        Assert.Equal(900, await check.BalanceOfAsync(userId, currency.Id));
        Assert.Single(await check.EntitlementsOfAsync(userId));
        Assert.Single(await check.TransactionsOfAsync(userId), t => t.State == TransactionState.Completed);
    }

    [Fact]
    public async Task Concurrent_purchases_of_different_offers_stop_at_the_balance()
    {
        // Each offer sells its own product, so already-owned never fires and the balance is the only
        // thing standing in the way: coins for three, ten simultaneous attempts.
        await using var setup = _fixture.CreateContext();
        var currency = await setup.CreateCurrencyAsync();
        var kind = await setup.CreateProductKindAsync();
        var userId = await TestData.CreateUserAsync(setup);
        await Credit(setup, userId, currency.Id, 300);

        var offerIds = new List<Guid>();

        for (var i = 0; i < 10; i++)
        {
            var product = await setup.CreateProductAsync(productKindId: kind.Id);
            offerIds.Add((await setup.CreateOfferAsync(currency.Id, [product.Id], price: 100)).Id);
        }

        var results = await Task.WhenAll(offerIds.Select(async offerId =>
        {
            await using var context = _fixture.CreateContext();
            return await Purchase(context).PurchaseAsync(userId, Request(offerId));
        }));

        Assert.Equal(3, results.Count(r => r.Succeeded));
        Assert.All(results.Where(r => !r.Succeeded),
            r => Assert.Equal(ApiErrors.InsufficientBalance.Code, r.Error!.Code));

        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.BalanceOfAsync(userId, currency.Id));
        Assert.Equal(3, (await check.EntitlementsOfAsync(userId)).Count);
    }

    [Fact]
    public async Task An_expired_offer_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(
            context, balance: 500, price: 100, products: 1,
            expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var result = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.OfferExpired.Code, result.Error!.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(500, await check.BalanceOfAsync(userId, currency.Id));
    }

    [Fact]
    public async Task An_unavailable_offer_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var (userId, _, offer, _) = await ShopAsync(
            context, balance: 500, price: 100, products: 1, availability: OfferAvailability.Unavailable);

        var result = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.OfferUnavailable.Code, result.Error!.Code);
    }

    [Fact]
    public async Task A_purchase_limit_is_per_account_not_per_offer()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var kind = await context.CreateProductKindAsync();
        var product = await context.CreateProductAsync(productKindId: kind.Id);

        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100, purchaseLimit: 1);

        var buyer = await TestData.CreateUserAsync(context);
        var other = await TestData.CreateUserAsync(context);
        await Credit(context, buyer, currency.Id, 1000);
        await Credit(context, other, currency.Id, 1000);

        Assert.True((await Purchase(context).PurchaseAsync(buyer, Request(offer.Id))).Succeeded);

        var second = await Purchase(context).PurchaseAsync(buyer, Request(offer.Id));

        Assert.False(second.Succeeded);
        Assert.Equal(ApiErrors.PurchaseLimitReached.Code, second.Error!.Code);

        // The limit belongs to the account, not the offer — someone else still has their own.
        Assert.True((await Purchase(context).PurchaseAsync(other, Request(offer.Id))).Succeeded);
    }

    [Fact]
    public async Task A_refused_attempt_does_not_consume_the_purchase_limit()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var kind = await context.CreateProductKindAsync();
        var product = await context.CreateProductAsync(productKindId: kind.Id);
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100, purchaseLimit: 1);

        var userId = await TestData.CreateUserAsync(context);

        // Broke: refused, and the one allowed purchase must survive it.
        var refused = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));
        Assert.Equal(ApiErrors.InsufficientBalance.Code, refused.Error!.Code);

        await Credit(context, userId, currency.Id, 500);

        Assert.True((await Purchase(context).PurchaseAsync(userId, Request(offer.Id))).Succeeded);
    }

    [Fact]
    public async Task A_purchase_limit_above_one_is_unreachable_while_every_grant_is_durable()
    {
        // Not a bug, but a sharp edge worth pinning: an entitlement is unique per (user, product),
        // so the second purchase of the same offer hands over nothing and is refused as ALREADY_OWNED
        // *before* the limit is anywhere near reached. A limit above 1 only becomes meaningful when
        // something consumable exists to sell. Setting purchaseLimit: 1 is what "buy once" means.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var kind = await context.CreateProductKindAsync();
        var product = await context.CreateProductAsync(productKindId: kind.Id);
        var offer = await context.CreateOfferAsync(currency.Id, [product.Id], price: 100, purchaseLimit: 5);

        var userId = await TestData.CreateUserAsync(context);
        await Credit(context, userId, currency.Id, 1000);

        Assert.True((await Purchase(context).PurchaseAsync(userId, Request(offer.Id))).Succeeded);

        var second = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));

        Assert.False(second.Succeeded);
        Assert.Equal(ApiErrors.AlreadyOwned.Code, second.Error!.Code);
    }

    [Fact]
    public async Task Buying_something_already_owned_in_full_is_refused_rather_than_charged()
    {
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(context, balance: 500, price: 100, products: 1);

        Assert.True((await Purchase(context).PurchaseAsync(userId, Request(offer.Id))).Succeeded);

        var second = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));

        Assert.False(second.Succeeded);
        Assert.Equal(ApiErrors.AlreadyOwned.Code, second.Error!.Code);

        // Charged once, not twice — entitlements are unique per (user, product), so a second grant
        // would have handed over nothing.
        await using var check = _fixture.CreateContext();
        Assert.Equal(400, await check.BalanceOfAsync(userId, currency.Id));
    }

    [Fact]
    public async Task A_bundle_containing_something_already_owned_still_completes()
    {
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var kind = await context.CreateProductKindAsync();
        var owned = await context.CreateProductAsync(productKindId: kind.Id);
        var wanted = await context.CreateProductAsync(productKindId: kind.Id);

        var userId = await TestData.CreateUserAsync(context);
        await Credit(context, userId, currency.Id, 500);
        await new EntitlementService(context).GrantAsync(userId, owned.Id, EntitlementSource.AdminGrant);

        var offer = await context.CreateOfferAsync(currency.Id, [owned.Id, wanted.Id], price: 100);

        var result = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        await using var check = _fixture.CreateContext();
        Assert.Equal(2, (await check.EntitlementsOfAsync(userId)).Count);
    }

    [Fact]
    public async Task A_retired_product_inside_an_offer_unwinds_the_whole_purchase()
    {
        // The atomicity requirement, exercised through a real failure rather than a fake one: the
        // debit has already happened when the grant is refused, and must not survive.
        await using var context = _fixture.CreateContext();
        var currency = await context.CreateCurrencyAsync();
        var kind = await context.CreateProductKindAsync();
        var live = await context.CreateProductAsync(productKindId: kind.Id);
        var retired = await context.CreateProductAsync(productKindId: kind.Id, active: false);

        var userId = await TestData.CreateUserAsync(context);
        await Credit(context, userId, currency.Id, 500);

        var offer = await context.CreateOfferAsync(currency.Id, [live.Id, retired.Id], price: 100);

        var result = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.ProductInactive.Code, result.Error!.Code);

        await using var check = _fixture.CreateContext();
        Assert.Equal(500, await check.BalanceOfAsync(userId, currency.Id));
        Assert.Empty(await check.EntitlementsOfAsync(userId));
        Assert.DoesNotContain(await check.LedgerOfAsync(userId), e => e.Amount < 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_purchase_works_with_no_request_id_at_all(string? requestId)
    {
        // `{ "offerId": "…" }` on its own has to be a valid request — the server supplies the
        // idempotency key when the client does not. What is lost is only retry protection, which a
        // caller that never retries never needed.
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(context, balance: 500, price: 100, products: 1);

        var result = await Purchase(context)
            .PurchaseAsync(userId, new PurchaseRequest { OfferId = offer.Id, RequestId = requestId });

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal("COMPLETED", result.Value!.State);

        await using var check = _fixture.CreateContext();
        Assert.Equal(400, await check.BalanceOfAsync(userId, currency.Id));

        // A key was still recorded — every transaction is addressable, generated or not.
        Assert.NotEmpty(Assert.Single(await check.TransactionsOfAsync(userId)).RequestId);
    }

    [Fact]
    public async Task Without_a_request_id_a_repeat_call_is_a_new_purchase_not_a_replay()
    {
        // The cost of omitting it, pinned down: two identical calls are two attempts. The second is
        // still refused here — but by already-owned, not by idempotency.
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(context, balance: 500, price: 100, products: 1);

        var first = await Purchase(context).PurchaseAsync(userId, new PurchaseRequest { OfferId = offer.Id });
        var second = await Purchase(context).PurchaseAsync(userId, new PurchaseRequest { OfferId = offer.Id });

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        Assert.False(second.Succeeded);
        Assert.Equal(ApiErrors.AlreadyOwned.Code, second.Error!.Code);
        Assert.False(second.Value!.Replayed);

        await using var check = _fixture.CreateContext();
        Assert.Equal(400, await check.BalanceOfAsync(userId, currency.Id));
    }

    [Fact]
    public async Task Buying_an_offer_that_does_not_exist_reports_not_found_without_a_transaction()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var result = await Purchase(context).PurchaseAsync(userId, Request(Guid.NewGuid()));

        Assert.False(result.Succeeded);
        Assert.Equal(ApiErrors.OfferNotFound.Code, result.Error!.Code);

        await using var check = _fixture.CreateContext();
        Assert.Empty(await check.TransactionsOfAsync(userId));
    }

    [Fact]
    public async Task A_free_offer_completes_without_touching_the_wallet()
    {
        await using var context = _fixture.CreateContext();
        var (userId, currency, offer, _) = await ShopAsync(context, balance: 0, price: 0, products: 1);

        var result = await Purchase(context).PurchaseAsync(userId, Request(offer.Id));

        // A zero delta is refused by the wallet by design, so a free offer has to skip the debit
        // rather than fail on it.
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        await using var check = _fixture.CreateContext();
        Assert.Single(await check.EntitlementsOfAsync(userId));
        Assert.Equal(0, await check.BalanceOfAsync(userId, currency.Id));
    }

    // ------------------------------------------------------------- fixtures

    private static PurchaseService Purchase(Share7.Infrastructure.Persistence.ApplicationDbContext context) =>
        new(context, new WalletService(context), new EntitlementService(context));

    private static PurchaseRequest Request(Guid offerId) =>
        new() { OfferId = offerId, RequestId = $"req_{Guid.NewGuid():N}" };

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

    private static async Task<(Guid UserId, Currency Currency, Offer Offer, List<Guid> ProductIds)> ShopAsync(
        Share7.Infrastructure.Persistence.ApplicationDbContext context,
        long balance,
        long price,
        int products,
        OfferAvailability availability = OfferAvailability.Available,
        int? purchaseLimit = null,
        DateTime? expiresAtUtc = null)
    {
        var currency = await context.CreateCurrencyAsync();
        var kind = await context.CreateProductKindAsync();

        var productIds = new List<Guid>();

        for (var i = 0; i < products; i++)
            productIds.Add((await context.CreateProductAsync(productKindId: kind.Id)).Id);

        var offer = await context.CreateOfferAsync(
            currency.Id, [.. productIds], price, availability: availability,
            purchaseLimit: purchaseLimit, expiresAtUtc: expiresAtUtc);

        var userId = await TestData.CreateUserAsync(context);

        if (balance > 0)
            await Credit(context, userId, currency.Id, balance);

        return (userId, currency, offer, productIds);
    }
}
