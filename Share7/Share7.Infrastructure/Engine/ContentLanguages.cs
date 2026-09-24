using Microsoft.EntityFrameworkCore;
using Share7.Application.Engine.Interfaces;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Engine;

/// <inheritdoc cref="IContentLanguages"/>
/// <remarks>Scoped: read once per request, then reused by everything in it.</remarks>
public sealed class ContentLanguages : IContentLanguages
{
    private readonly ApplicationDbContext _db;
    private IReadOnlyList<ContentLanguage>? _cached;

    public ContentLanguages(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ContentLanguage>> GetAsync(CancellationToken cancellationToken = default)
    {
        return _cached ??= await _db.Languages
            .AsNoTracking()
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Code)
            .Select(l => new ContentLanguage(
                l.Id, l.Code, l.Name, l.IsContentLanguage, l.RequiredToPublish, l.Direction, l.SortOrder))
            .ToListAsync(cancellationToken);
    }
}
