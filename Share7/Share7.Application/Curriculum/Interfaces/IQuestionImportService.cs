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
}
