using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Parses the admin's question sheet and publishes it as the lesson's next question version.
/// <para>
/// Sheet parsing and row validation live in <see cref="QuestionSheetParser"/>, shared with
/// <see cref="RecoveryQuestionImportService"/> — the two pools accept the same file format, so
/// the rules are defined once.
/// </para>
/// </summary>
public class QuestionImportService : IQuestionImportService
{
    private readonly ApplicationDbContext _dbContext;

    public QuestionImportService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<QuestionImportResult> ImportAsync(
        Guid lessonId,
        Guid langId,
        Stream excelStream,
        string fileName,
        bool hasHeaderRow = true,
        Guid? uploadedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Lessons.AnyAsync(l => l.Id == lessonId, cancellationToken))
            return QuestionImportResult.Failed(lessonId, langId, "Lesson not found.");

        // The lesson is language-independent now, so the sheet's language cannot be inferred
        // from it — it has to be supplied, and it has to be a real one.
        if (!await _dbContext.Languages.AnyAsync(l => l.Id == langId, cancellationToken))
            return QuestionImportResult.Failed(lessonId, langId, "Unknown language.");

        var parsed = QuestionSheetParser.Parse(excelStream, hasHeaderRow, out var errors);
        if (errors.Count > 0)
            return new QuestionImportResult { Succeeded = false, LessonId = lessonId, LangId = langId, Errors = errors };

        if (parsed.Count == 0)
            return QuestionImportResult.Failed(lessonId, langId, "The sheet contains no question rows.");

        var now = DateTime.UtcNow;

        var questionSet = await _dbContext.LessonQuestionSets
            .FirstOrDefaultAsync(s => s.LessonId == lessonId && s.LangId == langId, cancellationToken);

        var newVersion = (questionSet?.Version ?? 0) + 1;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Previous rows are kept, just retired — student progress may reference their ids.
        // Scoped to this language: uploading English must not retire the Arabic set.
        var replacedCount = await _dbContext.Questions
            .Where(q => q.LessonId == lessonId && q.LangId == langId && q.IsActive)
            .ExecuteUpdateAsync(
                s => s.SetProperty(q => q.IsActive, false).SetProperty(q => q.DeactivatedAt, now),
                cancellationToken);

        foreach (var row in parsed)
        {
            var question = new Question
            {
                Id = Guid.NewGuid(),
                LessonId = lessonId,
                LangId = langId,
                Text = row.QuestionText,
                Version = newVersion,
                IsActive = true,
                RowNumber = row.RowNumber,
                CreatedAt = now
            };

            // Column 2 is the correct answer by contract; the other two are distractors.
            // Order is preserved as-is — the client shuffles before assigning doors to lanes.
            var choices = new[] { row.CorrectAnswer, row.WrongAnswer1, row.WrongAnswer2 }
                .Select((text, index) => new QuestionChoice
                {
                    Id = Guid.NewGuid(),
                    QuestionId = question.Id,
                    Text = text,
                    OrderIndex = index
                })
                .ToList();

            question.CorrectChoiceId = choices[0].Id;
            question.Choices = choices;

            _dbContext.Questions.Add(question);
        }

        if (questionSet is null)
        {
            // First sheet for this lesson in this language — the row appears with version 1.
            _dbContext.LessonQuestionSets.Add(new LessonQuestionSet
            {
                LessonId = lessonId,
                LangId = langId,
                Version = newVersion
            });
        }
        else
        {
            questionSet.Version = newVersion;
        }

        _dbContext.LessonQuestionUploads.Add(new LessonQuestionUpload
        {
            Id = Guid.NewGuid(),
            LessonId = lessonId,
            LangId = langId,
            Version = newVersion,
            FileName = QuestionSheetParser.Truncate(fileName, 260),
            QuestionCount = parsed.Count,
            UploadedByUserId = uploadedByUserId,
            UploadedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new QuestionImportResult
        {
            Succeeded = true,
            LessonId = lessonId,
            LangId = langId,
            Version = newVersion,
            ImportedCount = parsed.Count,
            ReplacedCount = replacedCount
        };
    }
}
