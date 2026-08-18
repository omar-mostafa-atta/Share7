using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

/// <summary>
/// Read side of the recovery-question cache protocol — the exact mirror of
/// <see cref="ILessonQuestionService"/> over the secondary pool.
/// <para>
/// It reuses the same DTOs deliberately: the two pools have identical wire shapes, so a client
/// deserialises a recovery response with the same model it already uses for questions.
/// </para>
/// </summary>
public interface ILessonRecoveryQuestionService
{
    /// <summary>Version-only lookup. Returns null when the lesson does not exist.</summary>
    Task<LessonVersionDto?> GetVersionAsync(Guid lessonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch version lookup so the client can validate its whole recovery cache in one round trip
    /// instead of one call per lesson. Unknown lesson ids are omitted from the result.
    /// </summary>
    Task<IReadOnlyList<LessonVersionDto>> GetVersionsAsync(IEnumerable<Guid> lessonIds, CancellationToken cancellationToken = default);

    /// <summary>Active recovery question set for a lesson. Returns null when the lesson does not exist.</summary>
    Task<LessonQuestionsDto?> GetQuestionsAsync(Guid lessonId, CancellationToken cancellationToken = default);
}
