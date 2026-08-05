using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

/// <summary>
/// Write side of the curriculum tree. Each add attaches a node to an existing parent and
/// inherits that parent's language — callers never pass a language, which is what keeps the
/// EN and AR trees from getting cross-wired.
/// <para>
/// Deletes cascade to every descendant, so each one refuses by default when the node still
/// has children and reports what would be destroyed; pass <c>force</c> to go through with it.
/// </para>
/// </summary>
public interface ICurriculumAdminService
{
    Task<ServiceResult<TermDto>> AddTermToGradeAsync(Guid gradeId, string name, CancellationToken cancellationToken = default);

    Task<ServiceResult<SubjectDto>> AddSubjectToTermAsync(Guid termId, string name, CancellationToken cancellationToken = default);

    Task<ServiceResult<ChapterDto>> AddChapterToSubjectAsync(Guid subjectId, string name, CancellationToken cancellationToken = default);

    Task<ServiceResult<LessonDto>> AddLessonToChapterAsync(Guid chapterId, string name, CancellationToken cancellationToken = default);

    Task<ServiceResult<CurriculumNodeChildCounts>> DeleteTermAsync(Guid termId, bool force, CancellationToken cancellationToken = default);

    Task<ServiceResult<CurriculumNodeChildCounts>> DeleteSubjectAsync(Guid subjectId, bool force, CancellationToken cancellationToken = default);

    Task<ServiceResult<CurriculumNodeChildCounts>> DeleteChapterAsync(Guid chapterId, bool force, CancellationToken cancellationToken = default);

    Task<ServiceResult<CurriculumNodeChildCounts>> DeleteLessonAsync(Guid lessonId, bool force, CancellationToken cancellationToken = default);
}
