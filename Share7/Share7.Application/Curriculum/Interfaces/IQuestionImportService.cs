using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

public interface IQuestionImportService
{
    /// <summary>
    /// Parses an .xlsx question sheet and publishes it as the next version for a lesson.
    /// Column layout: 1 = question, 2 = correct answer, 3 = wrong answer, 4 = wrong answer.
    /// </summary>
    /// <param name="hasHeaderRow">When true the first row is treated as headers and skipped.</param>
    Task<QuestionImportResult> ImportAsync(
        Guid lessonId,
        Stream excelStream,
        string fileName,
        bool hasHeaderRow = true,
        Guid? uploadedByUserId = null,
        CancellationToken cancellationToken = default);
}
