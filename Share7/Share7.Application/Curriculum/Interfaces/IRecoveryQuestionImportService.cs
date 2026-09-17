using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

/// <summary>
/// Write side of the secondary pool — the mirror of <see cref="IQuestionImportService"/>. Same
/// sheet format, same validation, same all-or-nothing contract; a different table and a
/// different version counter.
/// </summary>
public interface IRecoveryQuestionImportService
{
    /// <summary>
    /// Parses an .xlsx recovery-question sheet and publishes it as the next recovery version for
    /// one lesson in one language. Column layout: 1 = question, 2 = correct answer,
    /// 3 = wrong answer, 4 = wrong answer — identical to the main question sheet.
    /// <para>
    /// The lesson itself is language-independent, so the sheet's language cannot be inferred
    /// from it — <paramref name="langId"/> says which of the lesson's recovery sets this upload
    /// replaces. Uploading English leaves the Arabic recovery set and its version untouched, and
    /// neither touches the main question set.
    /// </para>
    /// </summary>
    /// <param name="hasHeaderRow">When true the first row is treated as headers and skipped.</param>
    Task<QuestionImportResult> ImportAsync(
        Guid lessonId,
        Guid langId,
        Stream excelStream,
        string fileName,
        bool hasHeaderRow = true,
        Guid? uploadedByUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes recovery questions typed by hand as the next recovery version for one lesson in
    /// one language. The mirror of <see cref="IQuestionImportService.PublishManualAsync"/>: same
    /// request shape, same rules, the recovery pool and its own counter.
    /// </summary>
    Task<QuestionImportResult> PublishManualAsync(
        Guid lessonId,
        Guid langId,
        ManualQuestionSetRequest request,
        Guid? publishedByUserId = null,
        CancellationToken cancellationToken = default);
}
