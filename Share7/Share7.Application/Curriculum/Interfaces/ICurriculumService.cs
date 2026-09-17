using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

/// <summary>
/// Browse the curriculum tree. Every method is scoped to the caller's content language,
/// resolved from the access-token claim, so results only ever come from one language tree.
/// </summary>
public interface ICurriculumService
{
    Task<IReadOnlyList<TermDto>> GetTermsAsync(Guid? gradeId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(Guid? termId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChapterDto>> GetChaptersAsync(Guid subjectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LessonDto>> GetLessonsAsync(Guid chapterId, CancellationToken cancellationToken = default);
}
