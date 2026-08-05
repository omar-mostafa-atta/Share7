using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

public class LessonQuestionService : ILessonQuestionService
{
    private readonly ApplicationDbContext _dbContext;

    public LessonQuestionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LessonVersionDto?> GetVersionAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Lessons
            .Where(l => l.Id == lessonId)
            .Select(l => new LessonVersionDto
            {
                LessonId = l.Id,
                Version = l.QuestionsVersion,
                QuestionCount = l.Questions.Count(q => q.IsActive)
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

        return await _dbContext.Lessons
            .Where(l => ids.Contains(l.Id))
            .Select(l => new LessonVersionDto
            {
                LessonId = l.Id,
                Version = l.QuestionsVersion,
                QuestionCount = l.Questions.Count(q => q.IsActive)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<LessonQuestionsDto?> GetQuestionsAsync(Guid lessonId, CancellationToken cancellationToken = default)
    {
        var lesson = await _dbContext.Lessons
            .AsNoTracking()
            .Where(l => l.Id == lessonId)
            .Select(l => new { l.Id, l.LangId, l.QuestionsVersion })
            .FirstOrDefaultAsync(cancellationToken);

        if (lesson is null)
            return null;

        var questions = await _dbContext.Questions
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.IsActive)
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

        return new LessonQuestionsDto
        {
            LessonId = lesson.Id,
            LangId = lesson.LangId,
            Version = lesson.QuestionsVersion,
            Questions = questions
        };
    }
}
