using System.Text.Json;
using Share7.Application.Common.Models;
using Share7.Application.Engine.Interfaces;
using Share7.Application.Engine.Models;
using Share7.Application.Workspace.Models;
using Share7.Domain.Workspace;

namespace Share7.Application.Workspace.Interfaces;

/// <summary>The member behind a Studio session: role and scope, read fresh on every request.</summary>
public interface IStudioMemberResolver
{
    /// <summary>Null when the account is not an active content-team member.</summary>
    Task<StudioMember?> ResolveAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Drafts: started, edited (autosave, If-Match on the revision), checked as they are typed, sent for
/// review, withdrawn, discarded, and brought up to date when live content moves underneath them.
/// Nothing here touches what students see — only a release does.
/// </summary>
public interface IDraftService
{
    Task<ServiceResult<DraftDto>> CreateAsync(StudioMember member, CreateDraftRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftDto>> GetAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default);

    Task<DraftPageDto> ListAsync(StudioMember member, DraftQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftDto>> SaveAsync(StudioMember member, Guid draftId, SaveDraftRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ContentProblem>>> CheckAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftDto>> SubmitAsync(StudioMember member, Guid draftId, DraftActionRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftDto>> WithdrawAsync(StudioMember member, Guid draftId, DraftActionRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult> DiscardAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default);

    /// <summary>What is live now for the draft's target — what an out-of-date draft is merged against.</summary>
    Task<ServiceResult<JsonElement>> LiveNowAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftDto>> RebaseAsync(StudioMember member, Guid draftId, RebaseDraftRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftDiffDto>> DiffAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default);

    /// <summary>"I have this draft open." Returns who else does.</summary>
    Task<ServiceResult<IReadOnlyList<Staff.Models.PersonRefDto>>> PresenceAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reviews: the queue, approving or sending back, and the comments pinned to a draft. **Nobody
/// approves work they wrote any of**, and a reviewer's scope must cover the draft's place in the
/// curriculum and every language it changes.
/// </summary>
public interface IReviewService
{
    Task<IReadOnlyList<ReviewQueueItemDto>> QueueAsync(StudioMember member, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftDto>> ApproveAsync(StudioMember member, Guid draftId, DraftActionRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftDto>> RequestChangesAsync(StudioMember member, Guid draftId, DraftActionRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<DraftCommentDto>>> CommentsAsync(StudioMember member, Guid draftId, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftCommentDto>> AddCommentAsync(StudioMember member, Guid draftId, CommentRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftCommentDto>> EditCommentAsync(StudioMember member, Guid commentId, string body, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftCommentDto>> ResolveCommentAsync(StudioMember member, Guid commentId, bool resolved, CancellationToken cancellationToken = default);
}

/// <summary>
/// Releases — Leads only. Built from approved drafts, checked for anything that would stop them,
/// published now or at a scheduled time as one transaction, and rolled back as a new release.
/// </summary>
public interface IReleaseService
{
    Task<IReadOnlyList<ReleaseSummaryDto>> ListAsync(StudioMember member, ReleaseStatus? status, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReleaseDto>> GetAsync(StudioMember member, Guid releaseId, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReleaseDto>> CreateAsync(StudioMember member, CreateReleaseRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReleaseDto>> UpdateDraftsAsync(StudioMember member, Guid releaseId, UpdateReleaseDraftsRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReleaseDto>> PublishAsync(StudioMember member, Guid releaseId, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReleaseDto>> ScheduleAsync(StudioMember member, Guid releaseId, ScheduleReleaseRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReleaseDto>> CancelAsync(StudioMember member, Guid releaseId, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReleaseDto>> RollbackAsync(StudioMember member, Guid releaseId, RollbackReleaseRequest request, CancellationToken cancellationToken = default);

    /// <summary>The scheduler: publishes every scheduled release that is due. Returns how many went out.</summary>
    Task<int> PublishDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>The curriculum as the Studio browses it: statuses, open drafts, what is missing.</summary>
public interface IStudioCurriculumService
{
    Task<IReadOnlyList<ContentLanguage>> LanguagesAsync(CancellationToken cancellationToken = default);

    /// <summary>The children of a node — or the grades, for a null parent.</summary>
    Task<IReadOnlyList<StudioNodeDto>> ChildrenAsync(StudioMember member, Guid? parentId, bool includeRetired, CancellationToken cancellationToken = default);

    Task<ServiceResult<StudioNodeDetailDto>> NodeAsync(StudioMember member, Guid nodeId, CancellationToken cancellationToken = default);

    Task<ServiceResult<LessonWorkspaceDto>> LessonAsync(StudioMember member, Guid lessonId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuestionSearchHitDto>> SearchAsync(string text, Guid? underNodeId, Guid? langId, int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Excel, kept as a first-class way in: a trial run that saves nothing, turning a sheet into a
/// draft, a template for any set of languages, and a lesson exported back to a sheet.
/// </summary>
public interface IStudioImportService
{
    Task<ServiceResult<ImportTrialDto>> TrialAsync(Stream sheet, CancellationToken cancellationToken = default);

    Task<ServiceResult<DraftDto>> ImportToDraftAsync(
        StudioMember member, Guid draftId, int revision, Stream sheet, string fileName, CancellationToken cancellationToken = default);

    Task<byte[]> TemplateAsync(IReadOnlyList<string>? languageCodes, CancellationToken cancellationToken = default);

    /// <param name="fromDraft">The team's open draft of the lesson rather than what is live.</param>
    Task<ServiceResult<byte[]>> ExportAsync(StudioMember member, Guid lessonId, bool fromDraft, CancellationToken cancellationToken = default);
}

/// <summary>A member's inbox, their assignments, and the team's activity feed.</summary>
public interface IStudioInboxService
{
    Task<IReadOnlyList<StudioNotificationDto>> NotificationsAsync(StudioMember member, bool unreadOnly, int take, CancellationToken cancellationToken = default);

    /// <param name="ids">Null marks everything read.</param>
    Task MarkReadAsync(StudioMember member, IReadOnlyList<Guid>? ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssignmentDto>> AssignmentsAsync(StudioMember member, bool mine, bool includeClosed, CancellationToken cancellationToken = default);

    Task<ServiceResult<AssignmentDto>> AssignAsync(StudioMember member, CreateAssignmentRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<AssignmentDto>> CloseAssignmentAsync(StudioMember member, Guid assignmentId, WorkAssignmentStatus status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityItemDto>> ActivityAsync(StudioMember member, Guid? nodeId, long? before, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeammateDto>> TeamAsync(CancellationToken cancellationToken = default);
}
