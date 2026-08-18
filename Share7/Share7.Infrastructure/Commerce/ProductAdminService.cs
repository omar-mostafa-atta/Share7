using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Commerce;

public class ProductAdminService : IProductAdminService
{
    private const int NameMaxLength = 128;

    private readonly ApplicationDbContext _dbContext;

    public ProductAdminService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AdminProductDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var products = await _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Kind)
            .Include(p => p.Translations)
            .Include(p => p.Grants)
            .OrderBy(p => p.Key)
            .ToListAsync(cancellationToken);

        var ids = products.Select(p => p.Id).ToList();

        var ownerCounts = await _dbContext.Entitlements
            .AsNoTracking()
            .Where(e => ids.Contains(e.ProductId))
            .GroupBy(e => e.ProductId)
            .Select(group => new { ProductId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count, cancellationToken);

        var codes = await LanguageCodesAsync(cancellationToken);

        return products
            .Select(product => ToDto(product, ownerCounts.GetValueOrDefault(product.Id), codes))
            .ToList();
    }

    public async Task<ServiceResult<AdminProductDto>> GetAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await LoadAsync(productId, cancellationToken);

        if (product is null)
            return NotFound(productId);

        return ServiceResult<AdminProductDto>.Success(ToDto(
            product,
            await CountOwnersAsync(productId, cancellationToken),
            await LanguageCodesAsync(cancellationToken)));
    }

    public async Task<ServiceResult<AdminProductDto>> CreateAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var key = request.Key.Trim().ToLowerInvariant();

        if (await _dbContext.Products.AnyAsync(p => p.Key == key, cancellationToken))
            return ServiceResult<AdminProductDto>.Failure(
                ApiErrors.ProductKeyTaken,
                ServiceErrorKind.Conflict,
                $"A product with key '{key}' already exists.");

        if (!await _dbContext.ProductKinds.AnyAsync(k => k.Id == request.ProductKindId, cancellationToken))
            return KindNotFound(request.ProductKindId);

        var translations = await ValidateTranslationsAsync(request.Translations, cancellationToken);

        if (!translations.Succeeded)
            return Rewrap(translations);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Key = key,
            ImageUrl = Blank(request.ImageUrl),
            ProductKindId = request.ProductKindId,
            Active = request.Active,
            Translations = translations.Value!
                .Select(t => new ProductTranslation
                {
                    LangId = t.LangId,
                    Name = t.Name,
                    Description = t.Description
                })
                .ToList()
        };

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Read back for the kind name — the navigation is not populated by setting the foreign key.
        // A product is created with no grants: they are added through their own endpoints, and a
        // product granting nothing is a half-authored catalogue entry rather than an error.
        return await ReadBackAsync(product.Id, ownerCount: 0, cancellationToken);
    }

    public async Task<ServiceResult<AdminProductDto>> UpdateAsync(
        Guid productId,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .Include(p => p.Translations)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
            return NotFound(productId);

        if (!await _dbContext.ProductKinds.AnyAsync(k => k.Id == request.ProductKindId, cancellationToken))
            return KindNotFound(request.ProductKindId);

        var translations = await ValidateTranslationsAsync(request.Translations, cancellationToken);

        if (!translations.Succeeded)
            return Rewrap(translations);

        // Re-categorising an owned product is deliberately allowed. Kind describes how the client
        // reads the grants; it does not change which references it receives, so unlike editing the
        // grant set it cannot hand an existing owner something different. Fixing a miscategorised
        // product has to stay possible after it has sold.
        product.ImageUrl = Blank(request.ImageUrl);
        product.ProductKindId = request.ProductKindId;
        product.Active = request.Active;
        Replace(product.Translations, translations.Value!);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ReadBackAsync(productId, await CountOwnersAsync(productId, cancellationToken), cancellationToken);
    }

    public async Task<ServiceResult> DeleteAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await _dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        // Idempotent: a product that is already gone is the state the caller asked for.
        if (product is null)
            return ServiceResult.Success();

        var ownerCount = await CountOwnersAsync(productId, cancellationToken);

        // The Entitlements FK is Restrict, so the database refuses this too — but as a raw
        // SqlException the client cannot read. Check first and answer in the envelope.
        if (ownerCount > 0)
            return ServiceResult.Failure(
                ApiErrors.ProductOwned,
                ServiceErrorKind.Conflict,
                $"{ownerCount} account(s) own '{product.Key}'. Their entitlements resolve through it, so it cannot be deleted — retire it with active: false instead.",
                new Dictionary<string, object?> { ["ownerCount"] = ownerCount });

        // Grants and translations go with it by cascade; nothing owns them but the product.
        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult.Success();
    }

    // ------------------------------------------------------------- helpers

    private Task<ServiceResult<List<CommerceName>>> ValidateTranslationsAsync(
        IReadOnlyList<CommerceTranslationRequest>? supplied,
        CancellationToken cancellationToken) =>
        CommerceTranslationValidator.ValidateAsync(
            _dbContext, supplied, ApiErrors.ProductInvalid, NameMaxLength, cancellationToken);

    /// <summary>
    /// Rewrites the set in place rather than clearing and re-adding: the composite key is
    /// (ProductId, LangId), so an untouched language has to stay the same row.
    /// </summary>
    private static void Replace(ICollection<ProductTranslation> existing, List<CommerceName> wanted)
    {
        foreach (var stale in existing.Where(t => wanted.All(w => w.LangId != t.LangId)).ToList())
            existing.Remove(stale);

        foreach (var name in wanted)
        {
            var row = existing.FirstOrDefault(t => t.LangId == name.LangId);

            if (row is null)
                existing.Add(new ProductTranslation
                {
                    LangId = name.LangId,
                    Name = name.Name,
                    Description = name.Description
                });
            else
            {
                row.Name = name.Name;
                row.Description = name.Description;
            }
        }
    }

    private Task<Product?> LoadAsync(Guid productId, CancellationToken cancellationToken) =>
        _dbContext.Products
            .AsNoTracking()
            .Include(p => p.Kind)
            .Include(p => p.Translations)
            .Include(p => p.Grants)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

    private Task<int> CountOwnersAsync(Guid productId, CancellationToken cancellationToken) =>
        _dbContext.Entitlements.CountAsync(e => e.ProductId == productId, cancellationToken);

    private Task<Dictionary<Guid, string>> LanguageCodesAsync(CancellationToken cancellationToken) =>
        _dbContext.Languages.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);

    private async Task<ServiceResult<AdminProductDto>> ReadBackAsync(
        Guid productId,
        int ownerCount,
        CancellationToken cancellationToken)
    {
        var saved = await LoadAsync(productId, cancellationToken);

        return ServiceResult<AdminProductDto>.Success(
            ToDto(saved!, ownerCount, await LanguageCodesAsync(cancellationToken)));
    }

    private static AdminProductDto ToDto(
        Product product,
        int ownerCount,
        IReadOnlyDictionary<Guid, string> languageCodes)
    {
        var kindName = product.Kind?.Name ?? string.Empty;

        return new AdminProductDto
        {
            ProductId = product.Id,
            Key = product.Key,
            ImageUrl = product.ImageUrl,
            Active = product.Active,
            ProductKindId = product.ProductKindId,
            KindName = kindName,
            Kind = ProductKindName.ToWire(kindName),
            OwnerCount = ownerCount,
            Translations = CommerceTranslationValidator.ToDtos(
                product.Translations, t => t.LangId, t => t.Name, t => t.Description, languageCodes),
            Grants = CommerceMappings.ToAdminDtos(product.Grants, kindName)
        };
    }

    private static ServiceResult<AdminProductDto> NotFound(Guid productId) =>
        ServiceResult<AdminProductDto>.Failure(
            ApiErrors.ProductNotFound,
            ServiceErrorKind.NotFound,
            $"Product {productId} does not exist.");

    private static ServiceResult<AdminProductDto> KindNotFound(Guid productKindId) =>
        ServiceResult<AdminProductDto>.Failure(
            ApiErrors.ProductKindNotFound,
            ServiceErrorKind.NotFound,
            $"Product kind {productKindId} does not exist.");

    /// <summary>Carries a failure across result types without losing the code or details.</summary>
    private static ServiceResult<AdminProductDto> Rewrap<T>(ServiceResult<T> failure) => new()
    {
        ErrorKind = failure.ErrorKind,
        Error = failure.Error,
        Errors = failure.Errors,
        Details = failure.Details
    };

    private static string? Blank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
