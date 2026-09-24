using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Publishes a lesson's next <em>recovery</em> question version in one language, from a
/// spreadsheet or from questions typed into the admin console. The mirror of
/// <see cref="QuestionImportService"/> over the secondary pool: same file format, same content
/// rules, same all-or-nothing contract, with its own version counter. The pool lives in the item
/// bank now, and its old table is kept in step by the publisher.
/// </summary>
public class RecoveryQuestionImportService : IRecoveryQuestionImportService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILessonContentPublisher _publisher;

    public RecoveryQuestionImportService(ApplicationDbContext dbContext, ILessonContentPublisher publisher)
    {
        _dbContext = dbContext;
        _publisher = publisher;
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
        var target = await SingleLanguagePublish.ResolveTargetAsync(_dbContext, lessonId, langId, cancellationToken);
        if (target is not null)
            return target;

        var parsed = QuestionSheetParser.Parse(excelStream, hasHeaderRow, out var errors);
        if (errors.Count > 0)
            return new QuestionImportResult { Succeeded = false, LessonId = lessonId, LangId = langId, Errors = errors };

        if (parsed.Count == 0)
            return QuestionImportResult.Failed(lessonId, langId, "The sheet contains no question rows.");

        return await SingleLanguagePublish.PublishAsync(
            _dbContext, _publisher, lessonId, langId, NodeItemRole.Recovery,
            parsed, QuestionSetSource.ExcelUpload, fileName, uploadedByUserId, cancellationToken);
    }

    public async Task<QuestionImportResult> PublishManualAsync(
        Guid lessonId,
        Guid langId,
        ManualQuestionSetRequest request,
        Guid? publishedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var target = await SingleLanguagePublish.ResolveTargetAsync(_dbContext, lessonId, langId, cancellationToken);
        if (target is not null)
            return target;

        var prepared = await ManualQuestionPreparer.PrepareAsync(
            request,
            () => SingleLanguagePublish.LoadActiveAsync(_dbContext, lessonId, langId, NodeItemRole.Recovery, cancellationToken));

        if (prepared.Errors.Count > 0)
            return new QuestionImportResult
            {
                Succeeded = false,
                LessonId = lessonId,
                LangId = langId,
                Errors = prepared.Errors
            };

        return await SingleLanguagePublish.PublishAsync(
            _dbContext, _publisher, lessonId, langId, NodeItemRole.Recovery,
            prepared.Rows, QuestionSetSource.ManualEntry, string.Empty, publishedByUserId, cancellationToken);
    }
}
