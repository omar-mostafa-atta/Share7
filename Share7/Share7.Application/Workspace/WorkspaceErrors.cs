using Share7.Application.Common.Models;

namespace Share7.Application.Workspace;

/// <summary>
/// The Content Studio workspace's refusals, in the <c>{ code, messageKey, details }</c> envelope. The
/// Studio translates <see cref="ApiErrorCode.MessageKey"/>; <c>details</c> carries what it needs to
/// put the refusal next to the thing it is about. Add; do not rename.
/// </summary>
public static class WorkspaceErrors
{
    public static readonly ApiErrorCode DraftNotFound = new("DRAFT_NOT_FOUND", "errors.draft.notFound");

    /// <summary>Someone saved the draft since you loaded it. <c>details.revision</c> is the current one — reload and reapply.</summary>
    public static readonly ApiErrorCode DraftRevisionMoved = new("DRAFT_REVISION_MOVED", "errors.draft.revisionMoved");

    /// <summary>
    /// Live content changed after the draft was started. <c>details.changed</c> says what moved. Bring
    /// the draft up to date (which sends it back through review) before it can be released.
    /// </summary>
    public static readonly ApiErrorCode DraftOutOfDate = new("DRAFT_OUT_OF_DATE", "errors.draft.outOfDate");

    /// <summary>Released or discarded drafts are history; start a new one.</summary>
    public static readonly ApiErrorCode DraftClosed = new("DRAFT_CLOSED", "errors.draft.closed");

    /// <summary>The draft is not in a state that allows this. <c>details.status</c> is the state it is in.</summary>
    public static readonly ApiErrorCode DraftWrongStatus = new("DRAFT_WRONG_STATUS", "errors.draft.wrongStatus");

    /// <summary>The draft still breaks a rule. <c>details.problems</c> lists every one, placed on its field.</summary>
    public static readonly ApiErrorCode DraftHasProblems = new("DRAFT_HAS_PROBLEMS", "errors.draft.hasProblems");

    /// <summary>The proposal is not the shape its kind needs. <c>details.reason</c> names what is wrong.</summary>
    public static readonly ApiErrorCode DraftInvalid = new("DRAFT_INVALID", "errors.draft.invalid");

    /// <summary>
    /// Outside your scope. <c>details.reason</c>: <c>node</c> (that part of the curriculum),
    /// <c>languages</c> (<c>details.languages</c> lists the ones you may not change), or <c>role</c>.
    /// </summary>
    public static readonly ApiErrorCode OutOfScope = new("OUT_OF_SCOPE", "errors.scope.outOfScope");

    /// <summary>You wrote part of this draft, so a second person has to approve it.</summary>
    public static readonly ApiErrorCode OwnWork = new("OWN_WORK", "errors.review.ownWork");

    /// <summary>Practice drafts can be reviewed but never released.</summary>
    public static readonly ApiErrorCode PracticeNotReleasable = new("PRACTICE_NOT_RELEASABLE", "errors.release.practice");

    public static readonly ApiErrorCode ReleaseNotFound = new("RELEASE_NOT_FOUND", "errors.release.notFound");

    /// <summary>The release is not in a state that allows this. <c>details.status</c>.</summary>
    public static readonly ApiErrorCode ReleaseWrongStatus = new("RELEASE_WRONG_STATUS", "errors.release.wrongStatus");

    /// <summary>
    /// A draft in the release cannot go out as it is. <c>details.drafts</c> lists each with a
    /// <c>reason</c>: <c>notApproved</c>, <c>outOfDate</c>, <c>practice</c>, <c>closed</c>,
    /// <c>sameTarget</c> (two drafts change one thing), <c>needsDraft</c> (its new parent is in
    /// another draft that is not in this release).
    /// </summary>
    public static readonly ApiErrorCode ReleaseNotReady = new("RELEASE_NOT_READY", "errors.release.notReady");

    /// <summary>
    /// A change the release made has been changed again since, so putting it back would undo that
    /// later work too. <c>details.conflicts</c> names each; roll the later release back first.
    /// </summary>
    public static readonly ApiErrorCode RollbackBlocked = new("ROLLBACK_BLOCKED", "errors.release.rollbackBlocked");

    /// <summary>A rollback needs a reason, and a schedule needs a time in the future.</summary>
    public static readonly ApiErrorCode ReleaseInvalid = new("RELEASE_INVALID", "errors.release.invalid");

    /// <summary>The sheet could not be read. <c>details.problems</c> lists each row and field.</summary>
    public static readonly ApiErrorCode ImportInvalid = new("IMPORT_INVALID", "errors.import.invalid");

    public static readonly ApiErrorCode CommentNotFound = new("COMMENT_NOT_FOUND", "errors.comment.notFound");

    public static readonly ApiErrorCode AssignmentNotFound = new("ASSIGNMENT_NOT_FOUND", "errors.assignment.notFound");

    public static readonly ApiErrorCode NodeNotFound = new("NODE_NOT_FOUND", "errors.node.notFound");

    public static readonly ApiErrorCode CurriculumNotFound = new("CURRICULUM_NOT_FOUND", "errors.curriculum.notFound");

    /// <summary>
    /// A curriculum or its levels cannot be written as given. <c>details.problems</c> names each
    /// (a missing name, a level without one, fewer than one level, too many).
    /// </summary>
    public static readonly ApiErrorCode CurriculumInvalid = new("CURRICULUM_INVALID", "errors.curriculum.invalid");

    /// <summary>
    /// Something in the curriculum has already been released, so its levels are fixed: every node
    /// sits at a level, and changing the levels under it would leave it at one that no longer exists.
    /// </summary>
    public static readonly ApiErrorCode CurriculumLocked = new("CURRICULUM_LOCKED", "errors.curriculum.locked");

    /// <summary>The Egyptian curriculum is the one the game serves; its name and levels are not the Studio's to change.</summary>
    public static readonly ApiErrorCode CurriculumServed = new("CURRICULUM_SERVED", "errors.curriculum.served");
}
