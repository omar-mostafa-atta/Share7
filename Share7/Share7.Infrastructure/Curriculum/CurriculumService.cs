using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Browse the curriculum tree. The tree itself is language-independent — these methods do not
/// filter rows by language, they resolve each node's <i>name</i> into the caller's language.
/// A node with no translation in that language comes back with an empty name rather than
/// disappearing, which makes a missing translation visible instead of silently truncating
/// the tree.
/// </summary>
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

        var query = _dbContext.Terms.AsNoTracking();
        if (gradeId is not null)
            query = query.Where(t => t.GradeId == gradeId.Value);

        // Unfiltered, terms from every grade come back — group them by their grade's ladder
        // position so "First Term" repeats in a predictable order rather than at random.
        return await query
            .OrderBy(t => t.Grade!.Order)
            .ThenBy(t => t.Order)
            .Select(t => new TermDto
            {
                Id = t.Id,
                Name = t.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                GradeId = t.GradeId,
                Order = t.Order
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(Guid? termId = null, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var query = _dbContext.Subjects.AsNoTracking();
        if (termId is not null)
            query = query.Where(s => s.TermId == termId.Value);

        return await query
            .OrderBy(s => s.Term!.Order)
            .ThenBy(s => s.Order)
            .Select(s => new SubjectDto
            {
                Id = s.Id,
                Name = s.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                TermId = s.TermId,
                Order = s.Order
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChapterDto>> GetChaptersAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        return await _dbContext.Chapters
            .AsNoTracking()
            .Where(c => c.SubjectId == subjectId)
            .OrderBy(c => c.Order)
            .Select(c => new ChapterDto
            {
                Id = c.Id,
                Name = c.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                SubjectId = c.SubjectId,
                Order = c.Order
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LessonDto>> GetLessonsAsync(Guid chapterId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        // questionsVersion and hasQuestions are per language: the sheets are uploaded one
        // language at a time, so a lesson can be playable in English and not yet in Arabic.
        return await _dbContext.Lessons
            .AsNoTracking()
            .Where(l => l.ChapterId == chapterId)
            .OrderBy(l => l.Order)
            .Select(l => new LessonDto
            {
                Id = l.Id,
                Name = l.Translations.Where(x => x.LangId == langId).Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                LangId = langId,
                ChapterId = l.ChapterId,
                Order = l.Order,
                QuestionsVersion = l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault(),
                HasQuestions = l.QuestionSets.Any(s => s.LangId == langId && s.Version > 0)
            })
            .ToListAsync(cancellationToken);
    }
}
