using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Curriculum.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// Publishes a lesson's next main-pool question version in one language, from a spreadsheet or
/// from questions typed into the admin console.
/// <para>
/// Both paths converge on one engine publish after validation, so a hand-typed set and an uploaded
/// one are written identically — same retirement of what they replace, same version rule, same
/// audit row. Only how the questions were obtained differs, and only up to that point.
/// </para>
/// <para>
/// Sheet parsing lives in <see cref="QuestionSheetParser"/> and the content rules in
/// <see cref="QuestionContentRules"/>, both shared with <see cref="RecoveryQuestionImportService"/>
/// — the two pools accept the same questions, so what makes one valid is defined once.
/// </para>
/// </summary>
public class QuestionImportService : IQuestionImportService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILessonContentPublisher _publisher;

    public QuestionImportService(ApplicationDbContext dbContext, ILessonContentPublisher publisher)
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
            _dbContext, _publisher, lessonId, langId, NodeItemRole.Core,
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
            () => SingleLanguagePublish.LoadActiveAsync(_dbContext, lessonId, langId, NodeItemRole.Core, cancellationToken));

        if (prepared.Errors.Count > 0)
            return new QuestionImportResult
            {
                Succeeded = false,
                LessonId = lessonId,
                LangId = langId,
                Errors = prepared.Errors
            };

        return await SingleLanguagePublish.PublishAsync(
            _dbContext, _publisher, lessonId, langId, NodeItemRole.Core,
            prepared.Rows, QuestionSetSource.ManualEntry, string.Empty, publishedByUserId, cancellationToken);
    }
}
