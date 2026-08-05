using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Interfaces;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

public class LanguageService : ILanguageService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUser;

    public LanguageService(ApplicationDbContext dbContext, ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<LanguageDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Languages
            .OrderBy(l => l.Name)
            .Select(l => new LanguageDto { Id = l.Id, Name = l.Name, Code = l.Code })
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> ResolveCurrentAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: the token already carries it.
        if (_currentUser.PreferredLanguageId is { } fromClaim)
            return fromClaim;

        // Slow path, only for tokens minted before the claim existed.
        return await ResolveForUserAsync(_currentUser.UserId, cancellationToken);
    }

    public async Task<Guid> ResolveForUserAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        if (userId is null)
            return LanguageIds.English;

        var preferred = await _dbContext.Users
            .Where(u => u.Id == userId.Value)
            .Select(u => u.PreferredLanguageId)
            .FirstOrDefaultAsync(cancellationToken);

        return preferred ?? LanguageIds.English;
    }

    public async Task<bool> SetPreferredLanguageAsync(Guid userId, Guid langId, CancellationToken cancellationToken = default)
    {
        var languageExists = await _dbContext.Languages.AnyAsync(l => l.Id == langId, cancellationToken);
        if (!languageExists)
            return false;

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return false;

        user.PreferredLanguageId = langId;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
