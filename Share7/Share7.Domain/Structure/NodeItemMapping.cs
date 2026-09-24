using Share7.Domain.Content;

namespace Share7.Domain.Structure;

/// <summary>
/// node × item × role. **Nodes reference items; they do not own them.**
/// <para>
/// That inversion is what makes one item serve a national chapter, an IGCSE topic and a teacher's
/// revision week while pooling one set of statistics — which is also what makes calibration reach a
/// useful sample size (§10.4). Today's lesson-owned <c>Question.LessonId</c> is the opposite
/// arrangement and is kept, unchanged, while both exist.
/// </para>
/// <para>
/// <see cref="Role"/> preserves the main/recovery split as data, retiring the need for the parallel
/// <c>RecoveryQuestion</c> table family — although that retirement is not part of this phase, since
/// recovery questions are served but never answered through the attempt endpoint and so produce no
/// evidence to preserve.
/// </para>
/// </summary>
public class NodeItemMapping
{
    public Guid Id { get; set; }

    public Guid CurriculumVersionId { get; set; }

    public Guid NodeId { get; set; }
    public CurriculumNode? Node { get; set; }

    public Guid ItemId { get; set; }
    public Item? Item { get; set; }

    public NodeItemRole Role { get; set; } = NodeItemRole.Core;

    /// <summary>Presentation order within the role. Carried over from the sheet row number.</summary>
    public int Order { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Set when the item stops being part of this node. Never deleted while evidence names it.</summary>
    public DateTime? RemovedAtUtc { get; set; }
}
