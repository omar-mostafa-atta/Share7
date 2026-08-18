using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Domain.Commerce;
using Share7.Domain.Economy;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Commerce;

public class PurchaseService : IPurchaseService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWalletService _wallet;
    private readonly IEntitlementService _entitlements;

    public PurchaseService(
        ApplicationDbContext dbContext,
        IWalletService wallet,
        IEntitlementService entitlements)
    {
        _dbContext = dbContext;
        _wallet = wallet;
        _entitlements = entitlements;
    }

    public async Task<ServiceResult<PurchaseResponse>> PurchaseAsync(
        Guid userId,
        PurchaseRequest request,
        CancellationToken cancellationToken = default)
    {
        // Optional: a caller that sends only an offerId gets a server-generated key, which makes the
        // call work but leaves the retry unprotected — a generated id is new every time, so a
        // repeated call is a genuinely new purchase. Clients that can retry should send their own.
        var requestId = (request.RequestId ?? string.Empty).Trim();

        if (requestId.Length == 0)
            requestId = $"srv_{Guid.NewGuid():N}";

        // ---- 1. replay ------------------------------------------------------------------------
        // **Only a completed purchase replays.** Idempotency exists to stop a second charge when the
        // client never learned the outcome of the first — so it protects a purchase that took money.
        // A refusal took nothing, and there is nothing to protect; replaying one only means that
        // topping up and trying again with the same requestId returns the stale "no" forever, which
        // is exactly what a student would do after seeing "not enough coins".
        //
        // An offer that has since expired still replays as the completed purchase it was, because
        // that answer must never change.
        if (await FindCompletedAsync(userId, requestId, cancellationToken) is { } replayed)
            return await ReplayAsync(userId, replayed, cancellationToken);

        // ---- 2. resolve -----------------------------------------------------------------------
        var offer = await _dbContext.Offers
            .AsNoTracking()
            .Include(o => o.Currency)
            .Include(o => o.Products)
                .ThenInclude(op => op.Product!)
                    .ThenInclude(p => p.Kind)
            .Include(o => o.Products)
                .ThenInclude(op => op.Product!)
                    .ThenInclude(p => p.Grants)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId, cancellationToken);

        // No transaction row for this one: there is nothing to record it against, and an offer id
        // that does not exist is a client bug rather than a shopping outcome.
        if (offer is null)
            return ServiceResult<PurchaseResponse>.Failure(
                ApiErrors.OfferNotFound,
                ServiceErrorKind.NotFound,
                $"Offer {request.OfferId} does not exist.");

        var purchaseCount = await CountCompletedAsync(userId, offer.Id, cancellationToken);
        var productIds = offer.Products.Select(op => op.ProductId).ToList();

        // Buying something the account already owns in full would charge for nothing: entitlements
        // are unique per (user, product), so a second grant is a no-op. A bundle where only some
        // products are owned still goes through — the rest are worth paying for.
        var ownedCount = await _dbContext.Entitlements
            .AsNoTracking()
            .CountAsync(e => e.UserId == userId && productIds.Contains(e.ProductId), cancellationToken);

        // ---- 3. eligibility -------------------------------------------------------------------
        // **One evaluation**, shared with the shop listing, so the two cannot advertise and refuse
        // the same offer. It is not the last word on ownership — the grant loop below re-decides it
        // race-proof — but it is what stops the common case before any money moves.
        var eligibility = OfferRules.Evaluate(
            offer,
            purchaseCount,
            DateTime.UtcNow,
            ownsEverything: productIds.Count > 0 && ownedCount == productIds.Count);

        if (!eligibility.CanPurchase)
            return await RefuseAsync(userId, offer, requestId, eligibility.Error!, cancellationToken);

        // ---- 4. charge and grant, as one unit -------------------------------------------------
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // A free offer skips the wallet entirely. The wallet refuses a zero delta by design —
            // an entry that moves nothing is noise in an audit trail — so asking it to charge 0
            // would turn "this is free" into INVALID_AMOUNT.
            if (offer.Price > 0)
            {
                // The wallet joins this transaction rather than opening its own, so the debit cannot
                // commit independently of the grants below.
                var debit = await _wallet.ApplyAsync(new WalletMutation
                {
                    UserId = userId,
                    CurrencyId = offer.CurrencyId,
                    Delta = -offer.Price,
                    TransactionType = CurrencyTransactionType.Purchase,
                    SourceType = LedgerSourceType.Purchase,
                    SourceId = offer.Id.ToString(),
                    IdempotencyKey = requestId
                }, cancellationToken);

                if (!debit.Succeeded)
                {
                    // Almost always too few coins. Roll back first: the refusal has to be recorded
                    // outside this transaction or it would roll back with everything else.
                    await transaction.RollbackAsync(cancellationToken);
                    await transaction.DisposeAsync();
                    Detach();

                    return await RefuseAsync(
                        userId, offer, requestId, debit.Error ?? ApiErrors.InsufficientBalance, cancellationToken);
                }
            }

            var grantedSomething = false;

            foreach (var productId in productIds)
            {
                var granted = await _entitlements.GrantAsync(
                    userId, productId, EntitlementSource.Purchase, null, cancellationToken);

                // A retired product inside a live offer is an authoring mistake, and charging for it
                // would be worse than refusing. Everything unwinds, including the debit.
                if (!granted.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    await transaction.DisposeAsync();
                    Detach();

                    return await RefuseAsync(
                        userId, offer, requestId, granted.Error ?? ApiErrors.NotEligible, cancellationToken);
                }

                grantedSomething |= !granted.Value!.AlreadyOwned;
            }

            // **The already-owned check above is not enough on its own.** Two buys of the same offer
            // arriving together — a double-tapped button with two different requestIds — both read
            // "not owned", both debit, and the loser's grant silently no-ops against the unique
            // (user, product) index. Without this the account is charged twice for one item.
            //
            // Deciding it here instead, on whether anything was actually handed over, is race-proof:
            // whichever attempt grants nothing new unwinds its own debit and is refused.
            if (!grantedSomething)
            {
                await transaction.RollbackAsync(cancellationToken);
                await transaction.DisposeAsync();
                Detach();

                return await RefuseAsync(userId, offer, requestId, ApiErrors.AlreadyOwned, cancellationToken);
            }

            var record = new PurchaseTransaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OfferId = offer.Id,
                State = TransactionState.Completed,
                RequestId = requestId,
                Price = offer.Price,
                CurrencyId = offer.CurrencyId,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.PurchaseTransactions.Add(record);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await transaction.DisposeAsync();

            return ServiceResult<PurchaseResponse>.Success(
                await BuildAsync(userId, offer, record, cancellationToken));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Lost a race with a concurrent copy of this same requestId. The winner's transaction
            // stands and ours unwinds entirely — no second charge, no second grant. This is the
            // guarantee the read at step 1 only optimises; the unique index is what enforces it.
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            Detach();

            var winner = await FindCompletedAsync(userId, requestId, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Purchase hit a unique violation on (UserId, RequestId) but no completed transaction could be read back.");

            return await ReplayAsync(userId, winner, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            Detach();
            throw;
        }
    }

    // ------------------------------------------------------------- outcomes

    /// <summary>
    /// Records a refusal and returns it. Nothing was charged and nothing was granted, but the row
    /// is written anyway — an offer nobody can afford should be visible as refusals rather than as
    /// silence, and a support question about a missing purchase needs an answer either way.
    /// </summary>
    private async Task<ServiceResult<PurchaseResponse>> RefuseAsync(
        Guid userId,
        Offer offer,
        string requestId,
        ApiErrorCode error,
        CancellationToken cancellationToken)
    {
        // A purchase with this exact key may have completed since the check at the top — a genuine
        // retry that raced the original and lost. Replaying beats refusing: the caller asked "did my
        // purchase go through", and it did. Only reachable under concurrency, since the fresh path
        // already looked.
        if (await FindCompletedAsync(userId, requestId, cancellationToken) is { } winner)
            return await ReplayAsync(userId, winner, cancellationToken);

        var record = new PurchaseTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OfferId = offer.Id,
            State = TransactionState.Refused,
            RequestId = requestId,
            Price = offer.Price,
            CurrencyId = offer.CurrencyId,
            FailureReasonKey = error.MessageKey,
            CreatedAtUtc = DateTime.UtcNow
        };

        // No unique-violation handling here: the idempotency index is filtered to completed rows, so
        // a refusal never collides — a student who tries three times while broke leaves three rows.
        _dbContext.PurchaseTransactions.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildAsync(userId, offer, record, cancellationToken);

        return ServiceResult<PurchaseResponse>.Failure(
            error,
            ServiceErrorKind.Conflict,
            $"Purchase of offer {offer.Id} refused: {error.Code}.",
            response);
    }

    /// <summary>
    /// Returns an earlier **completed** purchase verbatim. Re-evaluating would be wrong: the answer
    /// to "did my purchase go through" must not change because the offer expired, sold out, or the
    /// balance moved in the meantime.
    /// <para>
    /// Refusals never reach here — they are re-evaluated instead, because nothing was charged and
    /// the condition that caused them is exactly the thing a retry is trying to fix.
    /// </para>
    /// </summary>
    private async Task<ServiceResult<PurchaseResponse>> ReplayAsync(
        Guid userId,
        PurchaseTransaction record,
        CancellationToken cancellationToken)
    {
        var offer = await _dbContext.Offers
            .AsNoTracking()
            .Include(o => o.Currency)
            .Include(o => o.Products)
                .ThenInclude(op => op.Product!)
                    .ThenInclude(p => p.Kind)
            .Include(o => o.Products)
                .ThenInclude(op => op.Product!)
                    .ThenInclude(p => p.Grants)
            .FirstAsync(o => o.Id == record.OfferId, cancellationToken);

        return ServiceResult<PurchaseResponse>.Success(
            await BuildAsync(userId, offer, record, cancellationToken, replayed: true));
    }

    // ------------------------------------------------------------- helpers

    private async Task<PurchaseResponse> BuildAsync(
        Guid userId,
        Offer offer,
        PurchaseTransaction record,
        CancellationToken cancellationToken,
        bool replayed = false)
    {
        var completed = record.State == TransactionState.Completed;
        var productIds = offer.Products.Select(op => op.ProductId).OrderBy(id => id).ToList();

        var entitlements = completed
            ? (await _entitlements.GetForUserAsync(userId, cancellationToken))
                .Where(e => productIds.Contains(e.ProductId))
                .ToList()
            : [];

        return new PurchaseResponse
        {
            State = WireEnum.ToWire(record.State),
            TransactionId = record.Id,
            TransactionAtUtc = DateTime.SpecifyKind(record.CreatedAtUtc, DateTimeKind.Utc),
            OfferId = offer.Id,
            // Nothing was granted on a refusal, so nothing is listed — the client must not apply a
            // cosmetic off the back of a failed purchase.
            ProductIds = completed ? productIds : [],
            Products = completed
                ? offer.Products
                    .Where(op => op.Product is not null)
                    .Select(op => new ProductDto
                    {
                        ProductId = op.ProductId,
                        Grants = CommerceMappings.ToClientDtos(
                            op.Product!.Grants, op.Product.Kind?.Name ?? string.Empty)
                    })
                    .OrderBy(p => p.ProductId)
                    .ToList()
                : [],
            Entitlements = entitlements,
            // Always present, refusal included: the client reconciles its wallet from this rather
            // than making a second call.
            Balances = await _wallet.GetBalancesAsync(userId, cancellationToken),
            FailureReasonKey = record.FailureReasonKey,
            Replayed = replayed
        };
    }

    /// <summary>
    /// The one transaction a requestId can replay. Refusals are deliberately not matched — several
    /// may share a requestId, which is why the unique index is filtered to completed rows.
    /// </summary>
    private Task<PurchaseTransaction?> FindCompletedAsync(
        Guid userId,
        string requestId,
        CancellationToken cancellationToken) =>
        _dbContext.PurchaseTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.UserId == userId && t.RequestId == requestId && t.State == TransactionState.Completed,
                cancellationToken);

    private Task<int> CountCompletedAsync(Guid userId, Guid offerId, CancellationToken cancellationToken) =>
        _dbContext.PurchaseTransactions
            .CountAsync(t => t.UserId == userId && t.OfferId == offerId
                             && t.State == TransactionState.Completed, cancellationToken);

    /// <summary>
    /// Drops everything the rolled-back attempt left pending. Without this the next SaveChanges on
    /// this context retries those inserts outside any transaction — the same trap the reward
    /// savepoints hit.
    /// </summary>
    private void Detach()
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries().ToList())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.State = EntityState.Detached;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
