using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

public class CurriculumService : ICurriculumService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public CurriculumService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<IReadOnlyList<TermDto>> GetTermsAsync(Guid? gradeId = null, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var query = _dbContext.Terms.AsNoTracking().Where(t => t.LangId == langId);
        if (gradeId is not null)
            query = query.Where(t => t.GradeId == gradeId.Value);

        return await query
            .OrderBy(t => t.Name)
            .Select(t => new TermDto { Id = t.Id, Name = t.Name, LangId = t.LangId, GradeId = t.GradeId })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(Guid? termId = null, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var query = _dbContext.Subjects.AsNoTracking().Where(s => s.LangId == langId);
        if (termId is not null)
            query = query.Where(s => s.TermId == termId.Value);

        return await query
            .OrderBy(s => s.Name)
            .Select(s => new SubjectDto { Id = s.Id, Name = s.Name, LangId = s.LangId, TermId = s.TermId })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChapterDto>> GetChaptersAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        // The language filter also means a subject id from the other tree returns nothing
        // rather than leaking content in a language the user did not ask for.
        return await _dbContext.Chapters
            .AsNoTracking()
            .Where(c => c.SubjectId == subjectId && c.LangId == langId)
            .OrderBy(c => c.Name)
            .Select(c => new ChapterDto { Id = c.Id, Name = c.Name, LangId = c.LangId, SubjectId = c.SubjectId })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LessonDto>> GetLessonsAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        return await _dbContext.Lessons
            .AsNoTracking()
            .Where(l => l.ChapterId == chapterId && l.LangId == langId)
            .OrderBy(l => l.Name)
            .Select(l => new LessonDto
            {
                Id = l.Id,
                Name = l.Name,
                LangId = l.LangId,
                ChapterId = l.ChapterId,
                QuestionsVersion = l.QuestionsVersion
            })
            .ToListAsync(cancellationToken);
    }
}
