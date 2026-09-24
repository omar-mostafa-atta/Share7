using Share7.Application.Common.Models;
using Share7.Application.Engine.Models;

namespace Share7.Application.Engine.Interfaces;

/// <summary>Reads what is live for a lesson, in the engine's language-generic shape.</summary>
public interface ILessonContentReader
{
    /// <summary>Null when the lesson does not exist or is retired.</summary>
    Task<LessonContentDto?> ReadAsync(Guid lessonId, CancellationToken cancellationToken = default);
}

/// <summary>
/// **The only thing that writes lesson content.** The paired sheet, the one-language upload, hand
/// entry and Studio releases are all adapters over it, so there is one place that decides what a
/// publish does to question ids, item versions, set versions and the compatibility tables.
/// <para>
/// What a publish guarantees:
/// </para>
/// <list type="bullet">
/// <item>A rendering that did not change keeps its row — same question id, same choice ids — so
/// nothing a device cached about it goes stale. Judged per language: rewording the Arabic of a
/// question leaves its English row exactly as it was.</item>
/// <item>The renderings that did change get new rows under one new version of their item; each
/// rendering stays on the item version whose content it shows.</item>
/// <item>A set's version goes up by exactly one when what the game receives for it changed, and
/// not otherwise. It never goes down.</item>
/// <item>Rows are retired, never deleted: progress and evidence name them.</item>
/// <item>The old tables (<c>LessonQuestionSets</c>, <c>LessonRecoveryQuestionSets</c>,
/// <c>RecoveryQuestions</c>, the upload history) are written in the same transaction.</item>
/// </list>
/// <para>
/// Joins the caller's transaction when there is one (a release applies many publishes as one), and
/// opens its own otherwise.
/// </para>
/// </summary>
public interface ILessonContentPublisher
{
    /// <summary>
    /// Validation failures come back as <see cref="ServiceErrorKind.Validation"/> with every problem
    /// in <c>Details["problems"]</c> (a list of <see cref="ContentProblem"/>); a moved expected
    /// version as <see cref="ServiceErrorKind.Conflict"/>.
    /// </summary>
    Task<ServiceResult<ContentPublishOutcome>> PublishAsync(
        ContentPublishRequest request, CancellationToken cancellationToken = default);

    /// <summary>The same checks a publish runs, without writing anything — the Studio's "checks as you type".</summary>
    Task<IReadOnlyList<ContentProblem>> CheckAsync(
        Guid lessonId,
        IReadOnlyList<ContentDraftItem> items,
        IReadOnlyList<ContentSetKey> covers,
        ContentRuleSet rules,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// **The only thing that writes the curriculum's structure.** Every change goes to the node first
/// and to the legacy typed row in the same transaction, touching only the nodes it concerns — no
/// full re-sync of the tree after each edit.
/// <para>
/// Nothing is ever deleted. "Delete" is retire: hidden from students, history kept, reversible.
/// When a change would leave a student stranded — the lesson they were on retired, or a lesson
/// moved ahead of the one they reached — the students affected are queued for an unlock repair
/// that gives them what finishing it would have given them, or opens the gap, so nobody loses
/// access or ends up with nothing to play.
/// </para>
/// </summary>
public interface ICurriculumStructureService
{
    Task<ServiceResult<StructureChangeDto>> CreateAsync(
        CreateNodeCommand command, EngineActor actor, CancellationToken cancellationToken = default);

    /// <param name="expectedRevision">When given, refuses if the node changed since — see CurriculumNode.Revision.</param>
    Task<ServiceResult<StructureChangeDto>> RenameAsync(
        Guid nodeId, IReadOnlyList<NodeTitle> titles, int? expectedRevision, EngineActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>Moves a node under another parent of the right level, at a position (null = last).</summary>
    Task<ServiceResult<StructureChangeDto>> MoveAsync(
        Guid nodeId, Guid newParentId, int? position, int? expectedRevision, EngineActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the live children of <paramref name="parentId"/> in the given order, numbered 1..n.
    /// The list must name every live child exactly once.
    /// </summary>
    Task<ServiceResult<StructureChangeDto>> ReorderAsync(
        Guid parentId, IReadOnlyList<Guid> orderedChildIds, int? expectedRevision, EngineActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>Retires the node and every live node under it, at one instant.</summary>
    Task<ServiceResult<StructureChangeDto>> RetireAsync(
        Guid nodeId, int? expectedRevision, EngineActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings back a retired node and exactly the nodes retired with it. It returns to its old
    /// position if that is free, and after the last live sibling if not.
    /// </summary>
    Task<ServiceResult<StructureChangeDto>> RestoreAsync(
        Guid nodeId, EngineActor actor, CancellationToken cancellationToken = default);

    Task<NodeStateDto?> GetAsync(Guid nodeId, CancellationToken cancellationToken = default);
}
