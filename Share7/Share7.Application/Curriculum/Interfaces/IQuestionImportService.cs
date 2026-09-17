using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

public interface IQuestionImportService
{
    /// <summary>
    /// Parses an .xlsx question sheet and publishes it as the next version for one lesson in
    /// one language. Column layout: 1 = question, 2 = correct answer, 3 = wrong answer,
    /// 4 = wrong answer.
    /// <para>
    /// The lesson itself is language-independent, so the sheet's language cannot be inferred
    /// from it — <paramref name="langId"/> says which of the lesson's question sets this
    /// upload replaces. Uploading English leaves the Arabic set and its version untouched.
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
    /// Publishes questions typed by hand as the next version for one lesson in one language — the
    /// same act as an upload, from a form instead of a file.
    /// <para>
    /// <c>Mode</c> decides whether they join the published set or replace it. Either way this
    /// produces a **new version**: the set is immutable by design, so appending republishes the
    /// existing questions alongside the new ones rather than inserting into what is there.
    /// </para>
    /// <para>
    /// All-or-nothing, exactly as the sheet path is — one invalid question rejects the request and
    /// leaves the current version untouched.
    /// </para>
    /// </summary>
    Task<QuestionImportResult> PublishManualAsync(
        Guid lessonId,
        Guid langId,
        ManualQuestionSetRequest request,
        Guid? publishedByUserId = null,
        CancellationToken cancellationToken = default);
}
