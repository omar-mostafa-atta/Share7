using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

/// <summary>
/// Write side of the curriculum tree. Each add attaches one language-independent node to an
/// existing parent, along with a name for every configured language — there is no per-language
/// tree to get cross-wired any more, but a node with a missing translation would be unnamed
/// for those students, so all of them are required up front.
/// <para>
/// Deletes cascade to every descendant, so each one refuses by default when the node still
/// has children and reports what would be destroyed; pass <c>force</c> to go through with it.
/// </para>
/// </summary>
public interface ICurriculumAdminService
{
    Task<ServiceResult<TermDto>> AddTermToGradeAsync(Guid gradeId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<SubjectDto>> AddSubjectToTermAsync(Guid termId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<ChapterDto>> AddChapterToSubjectAsync(Guid subjectId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<LessonDto>> AddLessonToChapterAsync(Guid chapterId, CreateCurriculumNodeRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<CurriculumNodeChildCounts>> DeleteTermAsync(Guid termId, bool force, CancellationToken cancellationToken = default);

    Task<ServiceResult<CurriculumNodeChildCounts>> DeleteSubjectAsync(Guid subjectId, bool force, CancellationToken cancellationToken = default);

    Task<ServiceResult<CurriculumNodeChildCounts>> DeleteChapterAsync(Guid chapterId, bool force, CancellationToken cancellationToken = default);

    Task<ServiceResult<CurriculumNodeChildCounts>> DeleteLessonAsync(Guid lessonId, bool force, CancellationToken cancellationToken = default);
}
