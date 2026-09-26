using System.Text.Json;
using Share7.Application.Engine.Models;
using Share7.Application.Staff.Models;
using Share7.Domain.Staff;
using Share7.Domain.Workspace;

namespace Share7.Application.Workspace.Models;

// ===========================================================================
// Who is asking
// ===========================================================================

/// <summary>
/// A signed-in content-team member as the workspace sees them: their Studio role and where they may
/// make changes. <b>Scope limits changing, never seeing</b> — everyone in the Studio can read all of
/// the curriculum and every draft, and comment on any of it.
/// </summary>
public sealed record StudioMember(
    Guid UserId,
    StudioRole Role,
    bool AllNodes,
    IReadOnlyList<string> NodePaths,
    bool AllLanguages,
    IReadOnlySet<Guid> LanguageIds)
{
    /// <summary>Whether <paramref name="path"/> is inside one of the member's parts of the curriculum.</summary>
    public bool CoversPath(string path) =>
        AllNodes || NodePaths.Any(p => path == p || path.StartsWith(p + "/", StringComparison.Ordinal));

    public bool CoversLanguage(Guid langId) => AllLanguages || LanguageIds.Contains(langId);

    public IReadOnlyList<Guid> LanguagesOutside(IEnumerable<Guid> langIds) =>
        AllLanguages ? [] : langIds.Where(l => !LanguageIds.Contains(l)).Distinct().ToList();

    public bool IsAtLeast(StudioRole role) => Role >= role;
}

// ===========================================================================
// What each kind of draft proposes
// ===========================================================================

/// <summary><see cref="DraftKind.LessonContent"/>: the lesson's questions, every pool and language.</summary>
public sealed record LessonContentProposal(IReadOnlyList<ContentDraftItem> Items);

/// <summary>
/// <see cref="DraftKind.NewNode"/>: a node that does not exist yet. A new lesson can carry its
/// questions too, so "add a lesson" is one draft and one review.
/// </summary>
public sealed record NewNodeProposal(IReadOnlyList<NodeTitle> Titles, int? Position, IReadOnlyList<ContentDraftItem>? Items);

public sealed record RenameProposal(IReadOnlyList<NodeTitle> Titles);

public sealed record MoveProposal(Guid NewParentId, int? Position);

public sealed record ReorderProposal(IReadOnlyList<Guid> OrderedChildIds);

/// <summary>
/// A proposed recovery rule for one node. <c>Clear</c> asks for the node's own rule to be taken
/// away so that whatever covers it from above applies again — which is a different thing from a
/// rule that happens to match its parent's numbers, and the only way to say it.
/// </summary>
public sealed record RecoveryRuleProposal(
    int AfterWrongAnswers,
    int QuestionsToServe,
    bool AllowRepeats,
    bool Clear);

/// <summary>The rule in force at a node when a draft was started, and where it was written.</summary>
public sealed record RecoveryRuleBase(
    int AfterWrongAnswers,
    int QuestionsToServe,
    bool AllowRepeats,
    bool IsOwn,
    Guid? FromNodeId,
    string? FromNodeTitle,
    int Revision);

// ===========================================================================
// Reading drafts
// ===========================================================================

/// <summary>One step of the way down to a node: grade, term, subject, chapter, lesson — titles in every language.</summary>
public sealed record TrailStepDto(Guid Id, string Kind, IReadOnlyList<NodeTitle> Titles);

public sealed record DraftSummaryDto
{
    public required Guid Id { get; init; }
    public required DraftKind Kind { get; init; }
    public required DraftStatus Status { get; init; }
    public required bool IsPractice { get; init; }
    public Guid? NodeId { get; init; }
    public Guid? ParentNodeId { get; init; }
    public string? NodeKind { get; init; }
    public required string Title { get; init; }

    /// <summary>Where it sits: grade → … → the node (or its parent, for a new node), in every language.</summary>
    public required IReadOnlyList<TrailStepDto> Trail { get; init; }

    public required PersonRefDto CreatedBy { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required PersonRefDto UpdatedBy { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public required int Revision { get; init; }

    /// <summary>Live content moved underneath it; it must be brought up to date before release.</summary>
    public required bool IsOutOfDate { get; init; }

    public required int OpenComments { get; init; }
    public Guid? ReleaseId { get; init; }
}

public sealed record DraftDto
{
    public required DraftSummaryDto Summary { get; init; }

    /// <summary>The proposed state, shaped by the kind (<see cref="LessonContentProposal"/>…).</summary>
    public required JsonElement Proposal { get; init; }

    /// <summary>The live state the draft started from, for the side-by-side and the diff.</summary>
    public required JsonElement Base { get; init; }

    /// <summary>What would stop it being submitted, each placed on its field. Empty means ready.</summary>
    public required IReadOnlyList<ContentProblem> Problems { get; init; }

    public required IReadOnlyList<Guid> LanguagesTouched { get; init; }
    public required IReadOnlyList<PersonRefDto> Contributors { get; init; }
    public required IReadOnlyList<ReviewDecisionDto> Reviews { get; init; }

    /// <summary>Who else has it open right now.</summary>
    public required IReadOnlyList<PersonRefDto> AlsoHere { get; init; }

    /// <summary>What the caller may do with it — the Studio shows or hides the buttons from these.</summary>
    public required DraftPermissionsDto Can { get; init; }
}

/// <param name="ReleaseNow">
/// A Lead may put an open draft live in one step — approved by them and released on its own — without
/// a second person. Never a practice draft, never one already waiting in a release.
/// </param>
public sealed record DraftPermissionsDto(bool Edit, bool Submit, bool Review, bool Discard, bool Release, bool ReleaseNow);

public sealed record ReviewDecisionDto(
    Guid Id, PersonRefDto Reviewer, ReviewVerdict Verdict, string? Note, int DraftRevision, bool IsCurrent, DateTime CreatedAtUtc);

public sealed record DraftCommentDto(
    Guid Id,
    Guid? ParentCommentId,
    PersonRefDto Author,
    string Body,
    JsonElement? Anchor,
    DateTime CreatedAtUtc,
    DateTime? EditedAtUtc,
    DateTime? ResolvedAtUtc,
    PersonRefDto? ResolvedBy);

/// <summary>What a draft changes, per question and language, or per field for a structural one.</summary>
public sealed record DraftDiffDto(IReadOnlyList<DiffLineDto> Lines);

/// <param name="Change"><c>added</c>, <c>changed</c>, <c>removed</c>, <c>moved</c>.</param>
/// <param name="Subject"><c>question</c>, <c>title</c>, <c>parent</c>, <c>position</c>, <c>order</c>, <c>state</c>.</param>
public sealed record DiffLineDto(
    string Change,
    string Subject,
    Guid? ItemId,
    Domain.Content.NodeItemRole? Role,
    int? Order,
    Guid? LangId,
    string? Before,
    string? After);

public sealed record DraftQuery(
    bool Mine = false,
    DraftStatus? Status = null,
    Guid? NodeId = null,
    Guid? UnderNodeId = null,
    bool IncludeClosed = false,
    bool IncludePractice = false,
    int Page = 1,
    int PageSize = 50);

public sealed record DraftPageDto(IReadOnlyList<DraftSummaryDto> Drafts, int Total, int Page, int PageSize);

// ===========================================================================
// Writing drafts
// ===========================================================================

/// <summary>
/// Starts a draft — or, for a target that already has an open draft of that kind, returns that one:
/// the team shares it.
/// </summary>
public sealed record CreateDraftRequest
{
    public required DraftKind Kind { get; init; }

    /// <summary>The node changed; for a reorder, the parent. Omitted for a new node and for practice.</summary>
    public Guid? NodeId { get; init; }

    /// <summary>For a new node: where it goes.</summary>
    public Guid? ParentNodeId { get; init; }

    /// <summary>For a new node: term, subject, chapter or lesson.</summary>
    public string? NodeKind { get; init; }

    /// <summary>A sandbox lesson draft, never tied to the live curriculum.</summary>
    public bool IsPractice { get; init; }

    /// <summary>The first proposal. Omitted: starts from the live state (or empty, for a new node).</summary>
    public JsonElement? Proposal { get; init; }
}

public sealed record SaveDraftRequest(int Revision, JsonElement Proposal);

public sealed record DraftActionRequest(int Revision, string? Note = null);

/// <summary>
/// Bringing an out-of-date draft up to date: the proposal the author merged against the new live
/// state (the Studio shows both side by side). Omitted for a structural draft, which keeps its
/// proposal and only takes the new base.
/// </summary>
public sealed record RebaseDraftRequest(int Revision, JsonElement? Proposal);

public sealed record CommentRequest(string Body, JsonElement? Anchor, Guid? ParentCommentId);

// ===========================================================================
// Reviews
// ===========================================================================

/// <param name="WaitingSince">When it was submitted — the queue is oldest first, and says how long each has waited.</param>
public sealed record ReviewQueueItemDto(DraftSummaryDto Draft, DateTime WaitingSince, bool CanReview, string? CannotReviewBecause);

// ===========================================================================
// Releases
// ===========================================================================

public sealed record CreateReleaseRequest(string Title, string? Notes, IReadOnlyList<Guid> DraftIds);

public sealed record UpdateReleaseDraftsRequest(IReadOnlyList<Guid> Add, IReadOnlyList<Guid> Remove);

public sealed record ScheduleReleaseRequest(DateTime PublishAtUtc);

public sealed record RollbackReleaseRequest(string Reason);

public sealed record ReleaseSummaryDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public required ReleaseStatus Status { get; init; }
    public required int DraftCount { get; init; }
    public required PersonRefDto CreatedBy { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? ScheduledForUtc { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
    public PersonRefDto? PublishedBy { get; init; }
    public Guid? RollbackOfReleaseId { get; init; }
    public Guid? RolledBackByReleaseId { get; init; }
    public string? Reason { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed record ReleaseDto
{
    public required ReleaseSummaryDto Summary { get; init; }

    /// <summary>What goes out (before publishing: the drafts; after: the applied entries).</summary>
    public required IReadOnlyList<ReleaseItemDto> Items { get; init; }

    /// <summary>What goes wrong if it were published now — empty means ready.</summary>
    public required IReadOnlyList<ReleaseBlockerDto> Blockers { get; init; }

    public required ReleaseImpactDto Impact { get; init; }
}

public sealed record ReleaseItemDto(
    Guid? DraftId,
    DraftKind Kind,
    Guid NodeId,
    string Title,
    IReadOnlyList<TrailStepDto> Trail,
    IReadOnlyList<DiffLineDto> Diff,
    JsonElement? Outcome);

/// <param name="Reason"><c>notApproved</c>, <c>outOfDate</c>, <c>practice</c>, <c>closed</c>, <c>sameTarget</c>, <c>needsDraft</c>, <c>outOfScope</c>.</param>
public sealed record ReleaseBlockerDto(Guid DraftId, string Reason, Guid? RelatedDraftId = null);

/// <summary>Who a release reaches — counted, never listed.</summary>
public sealed record ReleaseImpactDto(IReadOnlyList<ImpactLineDto> Lines);

/// <param name="Kind">
/// <c>questionsChanged</c> (students who have played the lesson), <c>orderChanged</c> (students part
/// way through the parent), <c>retired</c> (students who had it open), <c>moved</c>.
/// </param>
public sealed record ImpactLineDto(string Kind, Guid NodeId, IReadOnlyList<TrailStepDto> Trail, int Students);

// ===========================================================================
// The curriculum, as the Studio browses it
// ===========================================================================

public sealed record StudioNodeDto
{
    public required Guid Id { get; init; }
    public required string Kind { get; init; }
    public Guid? ParentId { get; init; }
    public required int Order { get; init; }
    public required int Revision { get; init; }
    public required IReadOnlyList<NodeTitle> Titles { get; init; }
    public required bool IsRetired { get; init; }
    public required int ChildCount { get; init; }

    /// <summary>Open drafts on this node, by kind — what the tree colours by.</summary>
    public required IReadOnlyList<DraftKind> OpenDrafts { get; init; }

    /// <summary>Lessons only: languages required to publish that have no main questions live.</summary>
    public IReadOnlyList<Guid>? MissingLanguages { get; init; }

    /// <summary>Lessons only: live main questions per language.</summary>
    public IReadOnlyDictionary<Guid, int>? QuestionCounts { get; init; }

    /// <summary>Whether the caller may change it (their scope covers it).</summary>
    public required bool InScope { get; init; }

    /// <summary>The curriculum it belongs to.</summary>
    public Guid CurriculumId { get; init; }

    /// <summary>Whether it is at its curriculum's played level: where questions are written.</summary>
    public bool IsPlayable { get; init; }
}

public sealed record StudioNodeDetailDto(StudioNodeDto Node, IReadOnlyList<TrailStepDto> Trail, IReadOnlyList<StudioNodeDto> Children);

/// <summary>A lesson's workspace: what is live, and the team's open draft of it if there is one.</summary>
public sealed record LessonWorkspaceDto(
    StudioNodeDto Lesson,
    IReadOnlyList<TrailStepDto> Trail,
    LessonContentDto Live,
    DraftSummaryDto? OpenDraft);

public sealed record QuestionSearchHitDto(
    Guid LessonId,
    IReadOnlyList<TrailStepDto> Trail,
    Guid ItemId,
    Domain.Content.NodeItemRole Role,
    int Order,
    Guid LangId,
    Guid QuestionId,
    string Text);

// ===========================================================================
// Excel
// ===========================================================================

/// <summary>A sheet read without saving anything: every row it would make, and every problem found.</summary>
public sealed record ImportTrialDto(
    IReadOnlyList<Guid> Languages,
    IReadOnlyList<ContentDraftItem> Items,
    IReadOnlyList<ImportProblemDto> Problems,
    int MainCount,
    int RecoveryCount);

/// <param name="Row">The spreadsheet row, 1-based as Excel shows it.</param>
/// <param name="Column">The column letter, when the problem is in one cell.</param>
public sealed record ImportProblemDto(int Row, string? Column, string Code, string Message);

// ===========================================================================
// Inbox: notifications, assignments, activity
// ===========================================================================

public sealed record StudioNotificationDto(
    Guid Id,
    string Kind,
    PersonRefDto? Actor,
    Guid? DraftId,
    Guid? ReleaseId,
    Guid? AssignmentId,
    Guid? NodeId,
    string? Title,
    DateTime CreatedAtUtc,
    bool IsRead);

public sealed record CreateAssignmentRequest(Guid NodeId, Guid AssigneeUserId, string? Note, DateOnly? DueOn);

public sealed record AssignmentDto(
    Guid Id,
    Guid NodeId,
    IReadOnlyList<TrailStepDto> Trail,
    PersonRefDto Assignee,
    PersonRefDto AssignedBy,
    string? Note,
    DateOnly? DueOn,
    WorkAssignmentStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ClosedAtUtc);

public sealed record ActivityItemDto(
    long Sequence,
    DateTime OccurredAtUtc,
    PersonRefDto? Actor,
    string Action,
    string Summary,
    string? TargetType,
    string? TargetId);

public sealed record TeammateDto(Guid UserId, string Name, StudioRole Role);

// ===========================================================================
// Curricula
// ===========================================================================

/// <summary>One level of a curriculum as the Studio shows it — "Topic", played or not.</summary>
public sealed record StudioLevelDto(string Key, int Depth, bool IsPlayable, IReadOnlyList<NodeTitle> Names);

/// <summary>
/// A curriculum on the Studio's board: the Egyptian one the game serves, or one declared in the
/// Studio and not played yet.
/// </summary>
public sealed record StudioCurriculumDto
{
    public required Guid Id { get; init; }
    public required Guid VersionId { get; init; }

    /// <summary>The node every level hangs from. Null for the Egyptian tree, whose grades are its top.</summary>
    public Guid? RootNodeId { get; init; }

    public required string Key { get; init; }

    /// <summary>Its name per language. A declared curriculum's root carries them; the Egyptian one is named by the Studio.</summary>
    public required IReadOnlyList<NodeTitle> Titles { get; init; }

    /// <summary>Whether the game serves it. Everything else is "not playable yet".</summary>
    public required bool IsServed { get; init; }

    /// <summary>
    /// How many of its top levels are fixed in place, because something has been released or proposed
    /// at the deepest of them: 0 when nothing has, so every level can change. Levels below the fixed
    /// ones can still be added, taken out and reordered, as long as one stays below them — the level
    /// that holds something is never made the played one. Every level's name can always be corrected.
    /// </summary>
    public required int FixedLevels { get; init; }

    /// <summary>Whether no level can be added or taken out at all: something sits at the played level.</summary>
    public required bool LevelsLocked { get; init; }

    /// <summary>Its levels below its own root, top first; the last one is the one that is played.</summary>
    public required IReadOnlyList<StudioLevelDto> Levels { get; init; }

    /// <summary>Live nodes at its top level.</summary>
    public required int TopCount { get; init; }

    /// <summary>Live nodes at its played level.</summary>
    public required int PlayableCount { get; init; }

    /// <summary>Whether the caller's scope reaches into it at all.</summary>
    public required bool InScope { get; init; }

    /// <summary>Whether the caller may rename it or change its levels: a Lead whose scope is the whole curriculum.</summary>
    public required bool CanManage { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}

/// <summary>A level as declared: its name in each language. Where it sits is its place in the list.</summary>
public sealed record CurriculumLevelRequest(IReadOnlyList<NodeTitle> Names);

/// <summary>A new curriculum: its name, and its levels from the top down to the one that is played.</summary>
public sealed record CreateCurriculumRequest(IReadOnlyList<NodeTitle> Titles, IReadOnlyList<CurriculumLevelRequest> Levels);

/// <summary>
/// A curriculum's name, and optionally its levels. Levels can be renamed at any time; adding or
/// removing one is refused once they are locked.
/// </summary>
public sealed record UpdateCurriculumRequest(IReadOnlyList<NodeTitle> Titles, IReadOnlyList<CurriculumLevelRequest>? Levels);
