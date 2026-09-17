using Microsoft.EntityFrameworkCore;
using Share7.Application.Commerce.Interfaces;
using Share7.Application.Commerce.Models;
using Share7.Application.Common.Models;
using Share7.Domain.Commerce;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Commerce;

public class ProductKindAdminService : IProductKindAdminService
{
    private const int NameMaxLength = 64;

    private readonly ApplicationDbContext _dbContext;

    public ProductKindAdminService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ProductKindDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var kinds = await _dbContext.ProductKinds
            .AsNoTracking()
            .Include(k => k.Translations)
            .OrderBy(k => k.Name)
            .ToListAsync(cancellationToken);

        var counts = await _dbContext.Products
            .AsNoTracking()
            .GroupBy(p => p.ProductKindId)
            .Select(group => new { KindId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.KindId, x => x.Count, cancellationToken);

        var codes = await LanguageCodesAsync(cancellationToken);

        return kinds.Select(kind => ToDto(kind, counts.GetValueOrDefault(kind.Id), codes)).ToList();
    }

    public async Task<ServiceResult<ProductKindDto>> GetAsync(
        Guid productKindId,
        CancellationToken cancellationToken = default)
    {
        var kind = await _dbContext.ProductKinds
            .AsNoTracking()
            .Include(k => k.Translations)
            .FirstOrDefaultAsync(k => k.Id == productKindId, cancellationToken);

        if (kind is null)
            return NotFound(productKindId);

        return ServiceResult<ProductKindDto>.Success(ToDto(
            kind,
            await CountProductsAsync(productKindId, cancellationToken),
            await LanguageCodesAsync(cancellationToken)));
    }

    public async Task<ServiceResult<ProductKindDto>> CreateAsync(
        CreateProductKindRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        if (name.Length == 0)
            return Invalid("A product kind needs a machine name — it is what the client reads as the grant kind.");

        var translations = await ValidateTranslationsAsync(request.Translations, cancellationToken);

        if (!translations.Succeeded)
            return Rewrap(translations);

        if (await FindByWireNameAsync(name, excluding: null, cancellationToken) is { } clash)
            return NameTaken(clash, name);

        var kind = new ProductKind
        {
            Id = Guid.NewGuid(),
            Name = name,
            Translations = translations.Value!
                .Select(t => new ProductKindTranslation
                {
                    LangId = t.LangId,
                    Name = t.Name,
                    Description = t.Description
                })
                .ToList()
        };

        _dbContext.ProductKinds.Add(kind);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ProductKindDto>.Success(
            ToDto(kind, productCount: 0, await LanguageCodesAsync(cancellationToken)));
    }

    public async Task<ServiceResult<ProductKindDto>> UpdateAsync(
        Guid productKindId,
        UpdateProductKindRequest request,
        CancellationToken cancellationToken = default)
    {
        var kind = await _dbContext.ProductKinds
            .Include(k => k.Translations)
            .FirstOrDefaultAsync(k => k.Id == productKindId, cancellationToken);

        if (kind is null)
            return NotFound(productKindId);

        var name = request.Name.Trim();

        if (name.Length == 0)
            return Invalid("A product kind needs a machine name — it is what the client reads as the grant kind.");

        var translations = await ValidateTranslationsAsync(request.Translations, cancellationToken);

        if (!translations.Succeeded)
            return Rewrap(translations);

        if (await FindByWireNameAsync(name, excluding: productKindId, cancellationToken) is { } clash)
            return NameTaken(clash, name);

        kind.Name = name;
        Replace(kind.Translations, translations.Value!);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ProductKindDto>.Success(ToDto(
            kind,
            await CountProductsAsync(productKindId, cancellationToken),
            await LanguageCodesAsync(cancellationToken)));
    }

    public async Task<ServiceResult> DeleteAsync(Guid productKindId, CancellationToken cancellationToken = default)
    {
        var kind = await _dbContext.ProductKinds
            .FirstOrDefaultAsync(k => k.Id == productKindId, cancellationToken);

        // Idempotent: a kind that is already gone is the state the caller asked for.
        if (kind is null)
            return ServiceResult.Success();

        var productCount = await CountProductsAsync(productKindId, cancellationToken);

        // The FK is Restrict, so the database would refuse this anyway — but as a raw SqlException
        // rather than something the client can read. Check first and answer in the envelope.
        if (productCount > 0)
            return ServiceResult.Failure(
                ApiErrors.ProductKindInUse,
                ServiceErrorKind.Conflict,
                $"{productCount} product(s) are still of kind '{kind.Name}'. Re-categorise them before deleting it.",
                new Dictionary<string, object?> { ["productCount"] = productCount });

        // Translations cascade with it.
        _dbContext.ProductKinds.Remove(kind);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult.Success();
    }

    // ------------------------------------------------------------- helpers

    private Task<ServiceResult<List<CommerceName>>> ValidateTranslationsAsync(
        IReadOnlyList<CommerceTranslationRequest>? supplied,
        CancellationToken cancellationToken) =>
        CommerceTranslationValidator.ValidateAsync(
            _dbContext, supplied, ApiErrors.ProductKindInvalid, NameMaxLength, cancellationToken);

    /// <summary>
    /// Rewrites the set in place rather than clearing and re-adding: the composite key is
    /// (ProductKindId, LangId), so an untouched language has to stay the same row.
    /// </summary>
    private static void Replace(ICollection<ProductKindTranslation> existing, List<CommerceName> wanted)
    {
        foreach (var stale in existing.Where(t => wanted.All(w => w.LangId != t.LangId)).ToList())
            existing.Remove(stale);

        foreach (var name in wanted)
        {
            var row = existing.FirstOrDefault(t => t.LangId == name.LangId);

            if (row is null)
                existing.Add(new ProductKindTranslation
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

    /// <summary>
    /// Finds a kind colliding with <paramref name="name"/> on the **wire** form rather than the
    /// stored text: "Content Pack" and "content-pack" are different rows but one token, and the
    /// client could not tell them apart. The unique index only catches the exact-text case.
    /// </summary>
    private async Task<ProductKind?> FindByWireNameAsync(
        string name,
        Guid? excluding,
        CancellationToken cancellationToken)
    {
        var wire = ProductKindName.ToWire(name);
        var query = _dbContext.ProductKinds.AsNoTracking();

        if (excluding is { } id)
            query = query.Where(k => k.Id != id);

        // Normalising is not translatable, and this is a small lookup table read only when a kind
        // is authored — a handful of rows in memory beats a shadow column to keep in step.
        var candidates = await query.ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(k => ProductKindName.ToWire(k.Name) == wire);
    }

    private Task<int> CountProductsAsync(Guid productKindId, CancellationToken cancellationToken) =>
        _dbContext.Products.CountAsync(p => p.ProductKindId == productKindId, cancellationToken);

    private Task<Dictionary<Guid, string>> LanguageCodesAsync(CancellationToken cancellationToken) =>
        _dbContext.Languages.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Code, cancellationToken);

    private static ProductKindDto ToDto(
        ProductKind kind,
        int productCount,
        IReadOnlyDictionary<Guid, string> languageCodes) => new()
    {
        ProductKindId = kind.Id,
        Name = kind.Name,
        Kind = ProductKindName.ToWire(kind.Name),
        ProductCount = productCount,
        Translations = CommerceTranslationValidator.ToDtos(
            kind.Translations, t => t.LangId, t => t.Name, t => t.Description, languageCodes)
    };

    private static ServiceResult<ProductKindDto> NotFound(Guid productKindId) =>
        ServiceResult<ProductKindDto>.Failure(
            ApiErrors.ProductKindNotFound,
            ServiceErrorKind.NotFound,
            $"Product kind {productKindId} does not exist.");

    private static ServiceResult<ProductKindDto> NameTaken(ProductKind clash, string name) =>
        ServiceResult<ProductKindDto>.Failure(
            ApiErrors.ProductKindNameTaken,
            ServiceErrorKind.Conflict,
            $"'{clash.Name}' already covers '{ProductKindName.ToWire(name)}' — the client would see one token for both.",
            new Dictionary<string, object?> { ["existingName"] = clash.Name });

    private static ServiceResult<ProductKindDto> Invalid(string message) =>
        ServiceResult<ProductKindDto>.Failure(ApiErrors.ProductKindInvalid, ServiceErrorKind.Validation, message);

    /// <summary>Carries a failure across result types without losing the code or details.</summary>
    private static ServiceResult<ProductKindDto> Rewrap<T>(ServiceResult<T> failure) => new()
    {
        ErrorKind = failure.ErrorKind,
        Error = failure.Error,
        Errors = failure.Errors,
        Details = failure.Details
    };
}
