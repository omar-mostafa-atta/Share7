using Share7.Domain.Content;

namespace Share7.Application.Content.Interfaces;

/// <summary>
/// Gives a published question the identity it needs to be measurable: an <see cref="Item"/> that
/// survives rewrites and languages, an immutable <see cref="ItemVersion"/>, a place in the
/// curriculum and a learning target to be about.
/// <para>
/// **Every writer of a <c>Question</c> row goes through this.** <c>Question.ItemVersionId</c> is
/// non-nullable precisely so that forgetting is a compile error rather than a silently unmeasurable
/// question — a row without identity accumulates no statistics, maps to no target, and produces
/// evidence that can never be interpreted.
/// </para>
/// <para>
/// Nothing here saves. Rows are added to the caller's change tracker so the whole publish stays one
/// transaction: content and identity are created together or not at all.
/// </para>
/// </summary>
public interface IItemIdentityMinter
{
    /// <summary>
    /// Resolves the item version for one row of one lesson's sheet, creating the item, the version,
    /// the node mapping and the placeholder target mapping as needed.
    /// <para>
    /// Idempotent within a unit of work: the English and Arabic renderings of one sheet row call
    /// this with the same arguments and must receive the same version, because they are the same
    /// item — that is the identity fault this whole layer exists to fix.
    /// </para>
    /// </summary>
    /// <param name="lessonId">The lesson the row belongs to. Also the curriculum node id.</param>
    /// <param name="rowNumber">The sheet row. With the lesson, it is the item's lineage key.</param>
    /// <param name="versionNumber">The publish version. A new number means a new item version.</param>
    /// <param name="role">What part the item plays at the node.</param>
    /// <param name="nowUtc">One timestamp for the whole publish.</param>
    /// <summary>
    /// Points a main-pool item at its lesson's placeholder learning target, creating the target the
    /// first time the lesson needs one. Nothing saves. Used by the content publisher for items it
    /// mints itself; recovery items are never mapped (they carry no evidence).
    /// </summary>
    Task EnsureLessonTargetAsync(
        Guid lessonId, Guid itemId, bool itemIsNew, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<ItemVersion> ResolveForLessonRowAsync(
        Guid lessonId,
        int rowNumber,
        int versionNumber,
        NodeItemRole role,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);
}
