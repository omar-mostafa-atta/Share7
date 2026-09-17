using Microsoft.EntityFrameworkCore;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Publishes a lesson's next question version, from a spreadsheet or from questions typed into the
/// admin console.
/// <para>
/// Both paths converge on <see cref="PublishAsync"/> after validation, so a hand-typed set and an
/// uploaded one are written identically — same retirement of the previous version, same version
/// bump, same audit row. Only how the questions were obtained differs, and only up to that point.
/// </para>
/// <para>
/// Sheet parsing lives in <see cref="QuestionSheetParser"/> and the content rules in
/// <see cref="QuestionContentRules"/>, both shared with
/// <see cref="RecoveryQuestionImportService"/> — the two pools accept the same questions, so what
/// makes one valid is defined once.
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

        // The lesson is language-independent now, so the language cannot be inferred from it —
        // it has to be supplied, and it has to be a real one.
        if (!await _dbContext.Languages.AnyAsync(l => l.Id == langId, cancellationToken))
            return QuestionImportResult.Failed(lessonId, langId, "Unknown language.");

        return null;
    }

    /// <summary>
    /// The currently published questions, flattened back into the shape a publish accepts. Only
    /// needed for an append, which republishes them alongside the new ones.
    /// </summary>
    private async Task<IReadOnlyList<ExistingQuestion>> LoadActiveAsync(
        Guid lessonId, Guid langId, CancellationToken cancellationToken)
    {
        var questions = await _dbContext.Questions
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
    /// Writes one new version: retires what is published, inserts the supplied questions, moves the
    /// version counter and records who did it. Everything in one transaction — a set that is half
    /// replaced is worse than one that was never touched.
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

        var questionSet = await _dbContext.LessonQuestionSets
            .FirstOrDefaultAsync(s => s.LessonId == lessonId && s.LangId == langId, cancellationToken);

        var newVersion = (questionSet?.Version ?? 0) + 1;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Previous rows are kept, just retired — student progress may reference their ids.
        // Scoped to this language: publishing English must not retire the Arabic set.
        var replacedCount = await _dbContext.Questions
            .Where(q => q.LessonId == lessonId && q.LangId == langId && q.IsActive)
            .ExecuteUpdateAsync(
                s => s.SetProperty(q => q.IsActive, false).SetProperty(q => q.DeactivatedAt, now),
                cancellationToken);

        foreach (var row in rows)
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

            // The correct answer is stored first by contract; the other two are distractors.
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
            // First publish for this lesson in this language — the row appears with version 1.
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

        // The unique index on (LessonId, LangId, Version) is what stops two concurrent publishes
        // both claiming this version number — the read above cannot, since both would read the
        // same current one. The loser fails its insert rather than silently overwriting.
        _dbContext.LessonQuestionUploads.Add(new LessonQuestionUpload
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
