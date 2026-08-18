using Share7.Application.Common.Models;
using Share7.Application.Progress.Models;
using Share7.Domain.Progress;

namespace Share7.Application.Progress.Interfaces;

/// <summary>
/// Recording and reading a student's progress. Everything is scoped to one game — the same
/// student's tree in one game is independent of every other.
/// </summary>
public interface IProgressService
{
    /// <summary>
    /// Records one run of a lesson. The score is recomputed server-side from the submitted
    /// choice ids; the client's own count is only echoed back for comparison. Also evaluates
    /// the unlock ladder and returns anything newly opened.
    /// </summary>
    Task<ServiceResult<AttemptResultDto>> SubmitAttemptAsync(
        Guid userId, SubmitAttemptRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<LessonProgressDto>> GetLessonProgressAsync(
        Guid userId, Guid gameId, Guid lessonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregate for a chapter, subject, term or grade — a <c>GROUP BY</c> over the lesson rows
    /// rather than a stored total, so adding a lesson to a chapter is reflected immediately
    /// instead of leaving stale rollups behind.
    /// </summary>
    Task<ServiceResult<NodeProgressDto>> GetNodeProgressAsync(
        Guid userId, Guid gameId, CurriculumNodeType nodeType, Guid nodeId, CancellationToken cancellationToken = default);

    /// <summary>Grade-level aggregate. Separate because grades sit above the unlock ladder.</summary>
    Task<ServiceResult<NodeProgressDto>> GetGradeProgressAsync(
        Guid userId, Guid gameId, Guid gradeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Questions the student did not get right on their last run. Filtered to questions that are
    /// still active, so a lesson whose sheet was re-uploaded reports nothing until it is replayed.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<WrongQuestionDto>>> GetWrongQuestionsAsync(
        Guid userId, Guid gameId, Guid lessonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The whole grade with availability and completion on every node.
    /// <paramref name="gradeId"/> defaults to the student's own grade from their profile.
    /// </summary>
    Task<ServiceResult<ProgressSnapshotDto>> GetSnapshotAsync(
        Guid userId, Guid gameId, Guid? gradeId, CancellationToken cancellationToken = default);
}
