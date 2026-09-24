using Share7.Domain.Curriculum;

namespace Share7.Domain.Content;

/// <summary>
/// What is being served for one node, in one pool, in one language — and the version number the
/// game caches it under. Composite key (NodeId, Role, LangId).
/// <para>
/// **This row backs the game's version protocol.** It generalises the two tables that used to do
/// the job for lessons (<c>LessonQuestionSets</c> for the main pool, <c>LessonRecoveryQuestionSets</c>
/// for the recovery pool), which are kept as compatibility copies — written in the same
/// transaction — until cutover. A missing row means version 0: nothing has ever been published
/// there, so the lesson is not playable in that language.
/// </para>
/// <para>
/// <see cref="Version"/> goes up by exactly one whenever what is served changes, and never goes
/// down. A rollback publishes a <i>new</i> version whose content matches an older one, so a device
/// cache can never mistake old content for current. A publish that changes nothing for this set
/// leaves the version where it is, so no device downloads anything it already has.
/// </para>
/// <para>
/// What is in the set is the <c>Questions</c> rows with this node, pool and language that are
/// <c>IsActive</c>, in <c>RowNumber</c> order.
/// </para>
/// </summary>
public class PublishedItemSet
{
    public Guid NodeId { get; set; }

    public NodeItemRole Role { get; set; }

    public Guid LangId { get; set; }

    public int Version { get; set; }

    /// <summary>How many questions the current version serves. Zero is a real, published state.</summary>
    public int ItemCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>
/// One row per version ever published for a set: who, when, how, and how many.
/// <para>
/// Generalises <c>LessonQuestionUploads</c> and <c>LessonRecoveryQuestionUploads</c> (still written
/// as compatibility copies until cutover) and adds the release that published it, when a Studio
/// release did. Append-only in practice; nothing edits a publication after the fact.
/// </para>
/// </summary>
public class ContentPublication
{
    public Guid Id { get; set; }

    public Guid NodeId { get; set; }

    public NodeItemRole Role { get; set; }

    public Guid LangId { get; set; }

    /// <summary>The version this publication produced. Unique per (node, pool, language).</summary>
    public int Version { get; set; }

    public QuestionSetSource Source { get; set; }

    /// <summary>The uploaded file's name for a sheet; empty otherwise.</summary>
    public string FileName { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    public Guid? PublishedByUserId { get; set; }

    public DateTime PublishedAtUtc { get; set; }

    /// <summary>The Studio release that published this version. Null for the old admin paths.</summary>
    public Guid? ReleaseId { get; set; }
}
