using Share7.Application.Common.Models;

namespace Share7.Application.Engine;

/// <summary>
/// Machine codes for refusals from the engine's writers. The old admin endpoints show the English
/// message that comes with each; the Studio looks the <see cref="ApiErrorCode.MessageKey"/> up in its
/// own English and Arabic files. Add; do not rename.
/// </summary>
public static class EngineErrors
{
    /// <summary>The content breaks a rule. <c>details.problems</c> lists every problem, each placed on its field.</summary>
    public static readonly ApiErrorCode ContentInvalid = new("CONTENT_INVALID", "errors.content.invalid");

    /// <summary>
    /// Live content changed after the caller last read it. <c>details.sets</c> lists the sets that
    /// moved, with the version expected and the version found.
    /// </summary>
    public static readonly ApiErrorCode ContentMoved = new("CONTENT_MOVED", "errors.content.moved");

    public static readonly ApiErrorCode NodeNotFound = new("NODE_NOT_FOUND", "errors.node.notFound");

    /// <summary>The node changed after the caller last read it. <c>details.revision</c> is the current one.</summary>
    public static readonly ApiErrorCode NodeMoved = new("NODE_MOVED", "errors.node.moved");

    /// <summary>A lesson goes under a chapter, a chapter under a subject, and so on.</summary>
    public static readonly ApiErrorCode NodeWrongParent = new("NODE_WRONG_PARENT", "errors.node.wrongParent");

    /// <summary>A sibling already has this name in this language. <c>details.langId</c>, <c>details.title</c>.</summary>
    public static readonly ApiErrorCode NodeNameTaken = new("NODE_NAME_TAKEN", "errors.node.nameTaken");

    /// <summary>Another live sibling holds the position (old admin paths only; the Studio makes room).</summary>
    public static readonly ApiErrorCode NodePositionTaken = new("NODE_POSITION_TAKEN", "errors.node.positionTaken");

    /// <summary>Grades are fixed: their ids are on student profiles and gate game modes.</summary>
    public static readonly ApiErrorCode NodeNotEditable = new("NODE_NOT_EDITABLE", "errors.node.notEditable");

    /// <summary>The parent is retired; restore it first.</summary>
    public static readonly ApiErrorCode NodeParentRetired = new("NODE_PARENT_RETIRED", "errors.node.parentRetired");

    /// <summary>
    /// A title is missing, blank, too long, or in an unknown language. <c>details.problems</c> lists
    /// them (<c>titleMissing</c>, <c>titleTooLong</c>, <c>unknownLanguage</c>, <c>duplicateLanguage</c>).
    /// </summary>
    public static readonly ApiErrorCode NodeTitlesInvalid = new("NODE_TITLES_INVALID", "errors.node.titlesInvalid");

    /// <summary>A reorder must list every live child exactly once.</summary>
    public static readonly ApiErrorCode NodeOrderInvalid = new("NODE_ORDER_INVALID", "errors.node.orderInvalid");

    /// <summary>Already retired (retire), or not retired (restore).</summary>
    public static readonly ApiErrorCode NodeStateInvalid = new("NODE_STATE_INVALID", "errors.node.stateInvalid");
}
