using Share7.Domain.LookUps;
using Share7.Domain.Structure;

namespace Share7.Domain.Content;

/// <summary>
/// One served rendering of an item — its text and answers in one language — at a playable node of
/// a curriculum the game does not serve yet.
/// <para>
/// **The twin of <c>Questions</c>, for everything that is not the Egyptian tree.** An item's
/// renderings in the Egyptian curriculum are rows of the game's own <c>Questions</c> table, and that
/// table's <c>LessonId</c> is a foreign key to the legacy <c>Lessons</c> — so a question under a
/// curriculum declared in the Studio has nowhere to live there without inventing a legacy lesson,
/// and a legacy lesson is exactly what the game would then serve. These rows are keyed to the node
/// instead, and nothing the game reads today ever reads them: that is the guarantee "authored now,
/// played later" rests on, held by the schema rather than by nobody asking.
/// </para>
/// <para>
/// Everything else about the question is shared with the Egyptian tree and is already node-keyed:
/// the item and its versions, the node's mapping to it, the published set and its version number,
/// the publication history, and what it measures. Only where its words are kept differs, which is
/// why the publisher writes both kinds of row through the same plan.
/// </para>
/// </summary>
public class NodeItemRendering
{
    public Guid Id { get; set; }

    public Guid NodeId { get; set; }
    public CurriculumNode? Node { get; set; }

    public Guid ItemVersionId { get; set; }
    public ItemVersion? ItemVersion { get; set; }

    public NodeItemRole Role { get; set; } = NodeItemRole.Core;

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public string Text { get; set; } = string.Empty;

    public Guid CorrectChoiceId { get; set; }

    /// <summary>The set version this rendering was first served under, as on a <c>Questions</c> row.</summary>
    public int Version { get; set; }

    /// <summary>Retired, never deleted: a later reader of evidence will name these rows.</summary>
    public bool IsActive { get; set; } = true;

    public int RowNumber { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? DeactivatedAtUtc { get; set; }

    public ICollection<NodeItemRenderingChoice> Choices { get; set; } = new List<NodeItemRenderingChoice>();
}

/// <summary>One answer of a <see cref="NodeItemRendering"/>, in the order it is offered.</summary>
public class NodeItemRenderingChoice
{
    public Guid Id { get; set; }

    public Guid RenderingId { get; set; }
    public NodeItemRendering? Rendering { get; set; }

    public string Text { get; set; } = string.Empty;

    public int OrderIndex { get; set; }
}
