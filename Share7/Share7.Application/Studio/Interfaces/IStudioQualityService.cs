using Share7.Application.Common.Models;
using Share7.Application.Measurement.Models;
using Share7.Application.Workspace.Models;
using Share7.Domain.Measurement;

namespace Share7.Application.Studio.Interfaces;

/// <summary>
/// What children's answers say about the questions themselves, as the content team sees it.
/// <para>
/// **Everything here is read.** A flag is not a thing to dismiss: a question being answered too
/// fast to have been read, or whose best distractor beats its key, is fixed by rewriting the
/// question — which means a draft, a reviewer and a release, exactly like every other change to
/// what a child is asked. The board's only actions are the two that change what an answer MEANS
/// rather than what the question says, and those are a Lead's, immediately, with a reason and a row
/// in the permanent record (decided 24 Sep 2026).
/// </para>
/// </summary>
public interface IStudioQualityService
{
    /// <summary>
    /// How much of the content can be spoken about at all.
    /// <para>
    /// Deliberately NOT the platform-wide measurement summary the Admin Console reads. That one
    /// counts every response and every observation in the database — whole-table counts that take
    /// half a minute on real data and answer a question about the platform's health rather than
    /// about this team's content. These five numbers are about the questions the team wrote.
    /// </para>
    /// </summary>
    Task<StudioQualitySummaryDto> SummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Flagged questions, worst first, cut to the member's part of the curriculum. A member who
    /// cannot open a lesson has no business reading how its questions are performing.
    /// </summary>
    Task<IReadOnlyList<FlaggedQuestionDto>> FlaggedAsync(
        StudioMember member, Guid langId, string? flag = null, Guid? nodeId = null,
        int take = 50, int skip = 0, CancellationToken cancellationToken = default);

    /// <summary>One question in full, with the way to the lesson that would fix it.</summary>
    Task<ServiceResult<FlaggedQuestionDto>> QuestionAsync(
        StudioMember member, Guid itemId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a question as an anchor, or takes the mark off.
    /// <para>
    /// **Cheap now, impossible retroactively.** An anchor only works if it was already being asked
    /// across cohorts and forms while the answers accumulated; deciding in two years that a question
    /// should have been one does not conjure the responses that would have linked the scales.
    /// </para>
    /// </summary>
    Task<ServiceResult<FlaggedQuestionDto>> SetAnchorAsync(
        StudioMember member, Guid itemId, bool isAnchor, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops one version of a question informing any measurement, with a reason that is kept.
    /// <para>
    /// **The answers are not touched.** The child did answer, that is a fact, and it stays. What
    /// changes is whether the answer is allowed to say anything about them — and the row explains,
    /// permanently, why the numbers moved.
    /// </para>
    /// </summary>
    Task<ServiceResult<ExclusionReportDto>> ExcludeAsync(
        StudioMember member, Guid itemVersionId, ObservationExclusionReason reason, string note,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A flagged question, and the one thing a member can do about it: open the lesson it is in.
/// </summary>
public sealed record FlaggedQuestionDto
{
    public required ItemQualityDto Quality { get; init; }

    /// <summary>The way down to the lesson, for the trail across the top of the board.</summary>
    public required IReadOnlyList<TrailStepDto> Trail { get; init; }

    /// <summary>The lesson's open draft, if the team already has one. Null means starting one is the move.</summary>
    public Guid? OpenDraftId { get; init; }

    /// <summary>False when the question sits outside this member's part of the curriculum.</summary>
    public required bool CanFix { get; init; }
}

public sealed record ExclusionReportDto(Guid ItemVersionId, int ObservationsExcluded, string Reason);

/// <summary>What can be said about the team's own questions, and what cannot be said yet.</summary>
public sealed record StudioQualitySummaryDto
{
    /// <summary>Live questions the game is serving.</summary>
    public required int Questions { get; init; }

    /// <summary>Questions with at least one answer behind them.</summary>
    public required int Answered { get; init; }

    /// <summary>Questions with enough first answers that a number about them means something.</summary>
    public required int EnoughToSay { get; init; }

    /// <summary>Questions mapped to no skill at all. **Nobody can measure these.**</summary>
    public required int Unmapped { get; init; }

    public required int Anchors { get; init; }

    /// <summary>Below this many first answers the platform reports no number, which is a statement.</summary>
    public required int ReportingFloor { get; init; }
}
