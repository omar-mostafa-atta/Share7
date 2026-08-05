using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

public interface ILessonQuestionService
{
    /// <summary>Version-only lookup. Returns null when the lesson does not exist.</summary>
    Task<LessonVersionDto?> GetVersionAsync(Guid lessonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch version lookup so the client can validate its whole cache in one round trip
    /// instead of one call per lesson. Unknown lesson ids are omitted from the result.
    /// </summary>
    Task<IReadOnlyList<LessonVersionDto>> GetVersionsAsync(IEnumerable<Guid> lessonIds, CancellationToken cancellationToken = default);

    /// <summary>Active question set for a lesson. Returns null when the lesson does not exist.</summary>
    Task<LessonQuestionsDto?> GetQuestionsAsync(Guid lessonId, CancellationToken cancellationToken = default);
}
