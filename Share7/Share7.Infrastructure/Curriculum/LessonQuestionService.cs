using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Read side of the question cache protocol. Every lookup is scoped to the caller's content
/// language: a lesson is one shared row, but its questions and their version are per language.
/// </summary>
public class LessonQuestionService : ILessonQuestionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILanguageService _languageService;

    public LessonQuestionService(ApplicationDbContext dbContext, ILanguageService languageService)
    {
        _dbContext = dbContext;
        _languageService = languageService;
    }

    public async Task<LessonVersionDto?> GetVersionAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        return await _dbContext.Lessons
            .Where(l => l.Id == lessonId)
            .Select(l => new LessonVersionDto
            {
                LessonId = l.Id,
                LangId = langId,
                // No question-set row means nothing has been uploaded in this language: version 0.
                Version = l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault(),
                QuestionCount = l.Questions.Count(q => q.IsActive && q.LangId == langId)
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LessonVersionDto>> GetVersionsAsync(
        IEnumerable<Guid> lessonIds,
        CancellationToken cancellationToken = default)
    {
        var ids = lessonIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        return await _dbContext.Lessons
            .Where(l => ids.Contains(l.Id))
            .Select(l => new LessonVersionDto
            {
                LessonId = l.Id,
                LangId = langId,
                Version = l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault(),
                QuestionCount = l.Questions.Count(q => q.IsActive && q.LangId == langId)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<LessonQuestionsDto?> GetQuestionsAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);

        var lesson = await _dbContext.Lessons
            .AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new
            {
                l.Id,
                Version = l.QuestionSets.Where(s => s.LangId == langId).Select(s => s.Version).FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (lesson is null)
            return null;

        var questions = await _dbContext.Questions
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.LangId == langId && q.IsActive)
            .OrderBy(q => q.RowNumber)
            .Select(q => new QuestionDto
            {
                QuestionId = q.Id,
                Text = q.Text,
                CorrectAnswerId = q.CorrectChoiceId,
                Answers = q.Choices
                    .OrderBy(c => c.OrderIndex)
                    .Select(c => new AnswerDto { Id = c.Id, Text = c.Text })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        // An empty list with version 0 is the "not playable in this language yet" case —
        // the lesson exists and is named, it just has no sheet uploaded for this language.
        return new LessonQuestionsDto
        {
            LessonId = lesson.Id,
            LangId = langId,
            Version = lesson.Version,
            Questions = questions
        };
    }
}
