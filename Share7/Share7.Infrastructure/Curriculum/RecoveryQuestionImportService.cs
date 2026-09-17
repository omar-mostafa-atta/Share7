using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Publishes a lesson's next <em>recovery</em> question version, from a spreadsheet or from
/// questions typed into the admin console. The mirror of <see cref="QuestionImportService"/> over
/// the secondary pool: same file format, same content rules, same all-or-nothing contract, with
/// separate tables and a separate version counter.
/// </summary>
public class RecoveryQuestionImportService : IRecoveryQuestionImportService
{
    private readonly ApplicationDbContext _dbContext;

    public RecoveryQuestionImportService(ApplicationDbContext dbContext)
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
        var target = await ResolveTargetAsync(lessonId, langId, cancellationToken);
        if (target is not null)
            return target;

        var parsed = QuestionSheetParser.Parse(excelStream, hasHeaderRow, out var errors);
        if (errors.Count > 0)
            return new QuestionImportResult { Succeeded = false, LessonId = lessonId, LangId = langId, Errors = errors };

        if (parsed.Count == 0)
            return QuestionImportResult.Failed(lessonId, langId, "The sheet contains no question rows.");

        return await PublishAsync(
            lessonId, langId, parsed, QuestionSetSource.ExcelUpload, fileName, uploadedByUserId, cancellationToken);
    }

    public async Task<QuestionImportResult> PublishManualAsync(
        Guid lessonId,
        Guid langId,
        ManualQuestionSetRequest request,
        Guid? publishedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var target = await ResolveTargetAsync(lessonId, langId, cancellationToken);
        if (target is not null)
            return target;

        var prepared = await ManualQuestionPreparer.PrepareAsync(
            request,
            () => LoadActiveAsync(lessonId, langId, cancellationToken));

        if (prepared.Errors.Count > 0)
            return new QuestionImportResult
            {
                Succeeded = false,
                LessonId = lessonId,
                LangId = langId,
                Errors = prepared.Errors
            };

        return await PublishAsync(
            lessonId, langId, prepared.Rows, QuestionSetSource.ManualEntry, string.Empty, publishedByUserId, cancellationToken);
    }

    /// <summary>
    /// Refuses a publish that has nowhere to land, before any parsing or validation work is done.
    /// Returns null when the target is real.
    /// </summary>
    private async Task<QuestionImportResult?> ResolveTargetAsync(
        Guid lessonId, Guid langId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Lessons.AnyAsync(l => l.Id == lessonId, cancellationToken))
            return QuestionImportResult.Failed(lessonId, langId, "Lesson not found.");

        // The lesson is language-independent, so the language cannot be inferred from it — it has
        // to be supplied, and it has to be a real one.
        if (!await _dbContext.Languages.AnyAsync(l => l.Id == langId, cancellationToken))
            return QuestionImportResult.Failed(lessonId, langId, "Unknown language.");

        return null;
    }

    /// <summary>
    /// The currently published recovery questions, flattened back into the shape a publish accepts.
    /// Only needed for an append, which republishes them alongside the new ones.
    /// </summary>
    private async Task<IReadOnlyList<ExistingQuestion>> LoadActiveAsync(
        Guid lessonId, Guid langId, CancellationToken cancellationToken)
    {
        var questions = await _dbContext.RecoveryQuestions
            .AsNoTracking()
            .Where(q => q.LessonId == lessonId && q.LangId == langId && q.IsActive)
            .OrderBy(q => q.RowNumber)
            .Select(q => new ExistingQuestion(
                q.Text,
                q.CorrectChoiceId,
                q.Choices
                    .OrderBy(c => c.OrderIndex)
                    .Select(c => new ExistingChoice(c.Id, c.Text))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return questions;
    }

    /// <summary>
    /// Writes one new recovery version: retires what is published, inserts the supplied questions,
    /// moves the recovery version counter and records who did it — all in one transaction.
    /// </summary>
    private async Task<QuestionImportResult> PublishAsync(
        Guid lessonId,
        Guid langId,
        IReadOnlyList<PublishableQuestion> rows,
        QuestionSetSource source,
        string fileName,
        Guid? publishedByUserId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var questionSet = await _dbContext.LessonRecoveryQuestionSets
            .FirstOrDefaultAsync(s => s.LessonId == lessonId && s.LangId == langId, cancellationToken);

        var newVersion = (questionSet?.Version ?? 0) + 1;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Previous rows are kept, just retired — anything that recorded a recovery answer may
        // reference their ids. Scoped to this language, and to the recovery pool: publishing here
        // never touches the main question set.
        var replacedCount = await _dbContext.RecoveryQuestions
            .Where(q => q.LessonId == lessonId && q.LangId == langId && q.IsActive)
            .ExecuteUpdateAsync(
                s => s.SetProperty(q => q.IsActive, false).SetProperty(q => q.DeactivatedAt, now),
                cancellationToken);

        foreach (var row in rows)
        {
            var question = new RecoveryQuestion
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

            // The correct answer is stored first by contract; the other two are distractors.
            // Order is preserved as-is — the client shuffles before assigning doors to lanes.
            var choices = new[] { row.CorrectAnswer, row.WrongAnswer1, row.WrongAnswer2 }
                .Select((text, index) => new RecoveryQuestionChoice
                {
                    Id = Guid.NewGuid(),
                    RecoveryQuestionId = question.Id,
                    Text = text,
                    OrderIndex = index
                })
                .ToList();

            question.CorrectChoiceId = choices[0].Id;
            question.Choices = choices;

            _dbContext.RecoveryQuestions.Add(question);
        }

        if (questionSet is null)
        {
            // First recovery publish for this lesson in this language — the row appears with version 1.
            _dbContext.LessonRecoveryQuestionSets.Add(new LessonRecoveryQuestionSet
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

        // The unique index on (LessonId, LangId, Version) is what stops two concurrent publishes
        // both claiming this version number — the read above cannot, since both would read the
        // same current one. The loser fails its insert rather than silently overwriting.
        _dbContext.LessonRecoveryQuestionUploads.Add(new LessonRecoveryQuestionUpload
        {
            Id = Guid.NewGuid(),
            LessonId = lessonId,
            LangId = langId,
            Version = newVersion,
            FileName = QuestionSheetParser.Truncate(fileName, 260),
            Source = source,
            QuestionCount = rows.Count,
            UploadedByUserId = publishedByUserId,
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
            ImportedCount = rows.Count,
            ReplacedCount = replacedCount
        };
    }
}
