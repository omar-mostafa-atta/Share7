using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Commerce;

public class EntitlementService : IEntitlementService
{
    private readonly ApplicationDbContext _dbContext;

    public EntitlementService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<EntitlementDto>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Deliberately unfiltered by Product.Active. A retired product is one the shop no longer
        // sells, not one the owner stopped owning.
        var entitlements = await _dbContext.Entitlements
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.GrantedAtUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        return entitlements.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<EntitlementGrantResult>> GrantAsync(
        Guid userId,
        Guid productId,
        EntitlementSource source,
        string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
            return ServiceResult<EntitlementGrantResult>.Failure(
                ApiErrors.ProductNotFound,
                ServiceErrorKind.NotFound,
                $"Product {productId} does not exist.");

        if (!product.Active)
            return ServiceResult<EntitlementGrantResult>.Failure(
                ApiErrors.ProductInactive,
                ServiceErrorKind.Validation,
                $"Product '{product.Key}' is retired and cannot be granted. Existing owners keep it.");

        var existing = await FindAsync(userId, productId, cancellationToken);

        if (existing is not null)
            return Owned(existing, alreadyOwned: true);

        var entitlement = new Entitlement
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductId = productId,
            GrantedAtUtc = DateTime.UtcNow,
            Source = source,
            SourceId = sourceId
        };

        _dbContext.Entitlements.Add(entitlement);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Lost a race with a concurrent grant. Already owned is the right answer, and the
            // winner's row is the one that stands — the index is what guarantees only one exists,
            // the read above only spares the common case from reaching it.
            _dbContext.Entry(entitlement).State = EntityState.Detached;

            var winner = await FindAsync(userId, productId, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Entitlement insert hit a unique violation but no existing row could be read back.");

            return Owned(winner, alreadyOwned: true);
        }

        return Owned(entitlement, alreadyOwned: false);
    }

    private Task<Entitlement?> FindAsync(Guid userId, Guid productId, CancellationToken cancellationToken) =>
        _dbContext.Entitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == userId && e.ProductId == productId, cancellationToken);

    private static ServiceResult<EntitlementGrantResult> Owned(Entitlement entitlement, bool alreadyOwned) =>
        ServiceResult<EntitlementGrantResult>.Success(new EntitlementGrantResult
        {
            Entitlement = ToDto(entitlement),
            AlreadyOwned = alreadyOwned
        });

    private static EntitlementDto ToDto(Entitlement entitlement) => new()
    {
        EntitlementId = entitlement.Id,
        ProductId = entitlement.ProductId,
        // SQL Server returns datetime2 with Kind = Unspecified, which serializes without a zone and
        // leaves the client guessing. The contract shows a trailing Z, so say so explicitly.
        GrantedAtUtc = DateTime.SpecifyKind(entitlement.GrantedAtUtc, DateTimeKind.Utc),
        Source = WireEnum.ToWire(entitlement.Source)
    };

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
