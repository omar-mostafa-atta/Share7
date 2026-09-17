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

        // Sorted by the ladder position, not by name — sorting "Grade 10" and "Grade 2"
        // alphabetically is what the Order column exists to fix.
        return await _dbContext.Grades
            .AsNoTracking()
            .OrderBy(g => g.Order)
            .Select(g => new GradeDto
            {
                Id = g.Id,
                Name = g.Translations
                    .Where(t => t.LangId == resolvedLangId)
                    .Select(t => t.Name)
                    .FirstOrDefault() ?? string.Empty,
                LangId = resolvedLangId,
                Order = g.Order
            })
            .ToListAsync(cancellationToken);
    }
}
