using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

public class GradeService : IGradeService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public GradeService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<GradeDto>> GetAllAsync(Guid? langId = null, CancellationToken cancellationToken = default)
    {
        var resolvedLangId = langId ?? await _languageService.ResolveCurrentAsync(cancellationToken);

        return await _dbContext.Grades
            .AsNoTracking()
            .Where(g => g.LangId == resolvedLangId)
            .OrderBy(g => g.Name)
            .Select(g => new GradeDto { Id = g.Id, Name = g.Name, LangId = g.LangId })
            .ToListAsync(cancellationToken);
    }
}
