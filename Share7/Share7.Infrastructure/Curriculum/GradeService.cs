using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Infrastructure.Engine.Reads;

namespace Share7.Infrastructure.Curriculum;

public class GradeService : IGradeService
{
    private readonly ICurriculumReads _reads;
    private readonly ILanguageService _languageService;

    public GradeService(ICurriculumReads reads, ILanguageService languageService)
    {
        _reads = reads;
        _languageService = languageService;
    }

    /// <summary>
    /// Sorted by the ladder position, not by name — sorting "Grade 10" and "Grade 2"
    /// alphabetically is what the Order column exists to fix.
    /// </summary>
    public async Task<IReadOnlyList<GradeDto>> GetAllAsync(Guid? langId = null, CancellationToken cancellationToken = default)
    {
        var resolvedLangId = langId ?? await _languageService.ResolveCurrentAsync(cancellationToken);
        return await _reads.GradesAsync(resolvedLangId, cancellationToken);
    }
}
