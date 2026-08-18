using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Commerce;

public class ProductGrantAdminService : IProductGrantAdminService
{
    private readonly ApplicationDbContext _dbContext;

    public ProductGrantAdminService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AdminProductGrantDto>> GetAllAsync(
        Guid? productId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ProductGrants
            .AsNoTracking()
            .Include(g => g.Product)
            .ThenInclude(p => p!.Kind)
            .AsQueryable();

        if (productId is { } id)
            query = query.Where(g => g.ProductId == id);

        var grants = await query
            .OrderBy(g => g.Product!.Key)
            .ThenBy(g => g.Reference)
            .ToListAsync(cancellationToken);

        return grants.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<AdminProductGrantDto>> GetAsync(
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        var grant = await LoadAsync(grantId, tracking: false, cancellationToken);

        return grant is null
            ? NotFound(grantId)
            : ServiceResult<AdminProductGrantDto>.Success(ToDto(grant));
    }

    public async Task<ServiceResult<AdminProductGrantDto>> CreateAsync(
        CreateProductGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Kind)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);

        if (product is null)
            return ProductNotFound(request.ProductId);

        var reference = request.Reference.Trim();

        if (reference.Length == 0)
            return Invalid("A grant reference cannot be blank — it is the client's id for the thing being granted.");

        if (request.Quantity < 1)
            return Invalid("A grant quantity must be at least 1.");

        if (await LockedAsync(product, cancellationToken) is { } locked)
            return locked;

        if (await _dbContext.ProductGrants.AnyAsync(
                g => g.ProductId == product.Id && g.Reference == reference, cancellationToken))
            return ReferenceTaken(product, reference);

        var grant = new ProductGrant
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Reference = reference,
            Quantity = request.Quantity
        };

        _dbContext.ProductGrants.Add(grant);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Lost a race with a concurrent add of the same reference. The index is what actually
            // guarantees one row; the check above only spares the common case from reaching it.
            _dbContext.Entry(grant).State = EntityState.Detached;
            return ReferenceTaken(product, reference);
        }

        // Mapped from the kind name directly rather than by hanging `product` off the tracked grant:
        // that product was read AsNoTracking, and attaching it through the navigation makes the
        // *next* SaveChanges on this context throw for a duplicate key it never meant to insert.
        return ServiceResult<AdminProductGrantDto>.Success(
            CommerceMappings.ToAdminDto(grant, product.Kind?.Name ?? string.Empty));
    }

    public async Task<ServiceResult<AdminProductGrantDto>> UpdateAsync(
        Guid grantId,
        UpdateProductGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        var grant = await LoadAsync(grantId, tracking: true, cancellationToken);

        if (grant is null)
            return NotFound(grantId);

        var reference = request.Reference.Trim();

        if (reference.Length == 0)
            return Invalid("A grant reference cannot be blank — it is the client's id for the thing being granted.");

        if (request.Quantity < 1)
            return Invalid("A grant quantity must be at least 1.");

        if (await LockedAsync(grant.Product!, cancellationToken) is { } locked)
            return locked;

        if (await _dbContext.ProductGrants.AnyAsync(
                g => g.ProductId == grant.ProductId && g.Reference == reference && g.Id != grantId,
                cancellationToken))
            return ReferenceTaken(grant.Product!, reference);

        grant.Reference = reference;
        grant.Quantity = request.Quantity;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<AdminProductGrantDto>.Success(ToDto(grant));
    }

    public async Task<ServiceResult> DeleteAsync(Guid grantId, CancellationToken cancellationToken = default)
    {
        var grant = await LoadAsync(grantId, tracking: true, cancellationToken);

        // Idempotent: a grant that is already gone is the state the caller asked for.
        if (grant is null)
            return ServiceResult.Success();

        if (await LockedAsync(grant.Product!, cancellationToken) is { } locked)
            return locked;

        _dbContext.ProductGrants.Remove(grant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult.Success();
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// The rule that makes the whole entitlement chain safe: what a product hands over stops being
    /// editable the moment an account owns it.
    /// <para>
    /// An entitlement records only *that* an account owns a product — what it actually gets is these
    /// rows, read fresh on every resolution. So adding, editing or deleting one after a sale changes
    /// what existing owners have, retroactively and silently. Author a replacement product instead.
    /// </para>
    /// </summary>
    private async Task<ServiceResult<AdminProductGrantDto>?> LockedAsync(
        Product product,
        CancellationToken cancellationToken)
    {
        var ownerCount = await _dbContext.Entitlements
            .CountAsync(e => e.ProductId == product.Id, cancellationToken);

        if (ownerCount == 0)
            return null;

        return ServiceResult<AdminProductGrantDto>.Failure(
            ApiErrors.ProductGrantsLocked,
            ServiceErrorKind.Conflict,
            $"{ownerCount} account(s) already own '{product.Key}', so its grants cannot change. Author a replacement product instead.",
            new Dictionary<string, object?> { ["ownerCount"] = ownerCount });
    }

    private Task<ProductGrant?> LoadAsync(Guid grantId, bool tracking, CancellationToken cancellationToken)
    {
        var query = _dbContext.ProductGrants
            .Include(g => g.Product)
            .ThenInclude(p => p!.Kind)
            .AsQueryable();

        if (!tracking)
            query = query.AsNoTracking();

        return query.FirstOrDefaultAsync(g => g.Id == grantId, cancellationToken);
    }

    private static AdminProductGrantDto ToDto(ProductGrant grant) =>
        CommerceMappings.ToAdminDto(grant, grant.Product?.Kind?.Name ?? string.Empty);

    private static ServiceResult<AdminProductGrantDto> NotFound(Guid grantId) =>
        ServiceResult<AdminProductGrantDto>.Failure(
            ApiErrors.ProductGrantNotFound,
            ServiceErrorKind.NotFound,
            $"Product grant {grantId} does not exist.");

    private static ServiceResult<AdminProductGrantDto> ProductNotFound(Guid productId) =>
        ServiceResult<AdminProductGrantDto>.Failure(
            ApiErrors.ProductNotFound,
            ServiceErrorKind.NotFound,
            $"Product {productId} does not exist.");

    private static ServiceResult<AdminProductGrantDto> ReferenceTaken(Product product, string reference) =>
        ServiceResult<AdminProductGrantDto>.Failure(
            ApiErrors.ProductGrantReferenceTaken,
            ServiceErrorKind.Conflict,
            $"'{product.Key}' already grants '{reference}'. Change that grant's quantity instead of adding a second row.",
            new Dictionary<string, object?> { ["reference"] = reference });

    private static ServiceResult<AdminProductGrantDto> Invalid(string message) =>
        ServiceResult<AdminProductGrantDto>.Failure(
            ApiErrors.ProductGrantInvalid,
            ServiceErrorKind.Validation,
            message);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
