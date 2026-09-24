using Share7.Domain.Content;

namespace Share7.Domain.Competency;

/// <summary>
/// A typed edge in the competency graph. Direction matters and is not symmetric: reversing a
/// <see cref="LearningTargetEdgeKind.PrerequisiteOf"/> edge states the opposite of the truth.
/// </summary>
public class LearningTargetEdge
{
    public Guid Id { get; set; }

    /// <summary>The component, or the prerequisite, or the target being replaced.</summary>
    public Guid FromTargetId { get; set; }
    public LearningTarget? FromTarget { get; set; }

    /// <summary>The whole, or the thing it unlocks, or the replacement.</summary>
    public Guid ToTargetId { get; set; }
    public LearningTarget? ToTarget { get; set; }

    public LearningTargetEdgeKind EdgeKind { get; set; }

    /// <summary>
    /// How much of the destination this component accounts for, for aggregation. Only meaningful on
    /// <see cref="LearningTargetEdgeKind.ComponentOf"/> edges; 1.0 means "weight it like its siblings".
    /// </summary>
    public decimal Weight { get; set; } = 1.0m;

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Cross-framework equivalence, with a strength and a reviewer. This is what lets a learner move
/// from the national curriculum to IGCSE without their history being thrown away — and what stops
/// a <see cref="AlignmentStrength.Partial"/> overlap being treated as the same claim.
/// </summary>
public class LearningTargetAlignment
{
    public Guid Id { get; set; }

    public Guid SourceTargetId { get; set; }
    public LearningTarget? SourceTarget { get; set; }

    public Guid AlignedTargetId { get; set; }
    public LearningTarget? AlignedTarget { get; set; }

    public AlignmentStrength Strength { get; set; }

    /// <summary>
    /// Who asserted it. **Required in practice even though the column is nullable** — an unreviewed
    /// alignment is a guess, and a guess that moves measurements between curricula is worse than no
    /// alignment at all. Null only for rows created by a migration, which are marked
    /// <see cref="AlignmentStrength.Partial"/> and therefore never transfer a measurement.
    /// </summary>
    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// item × target × emphasis. **The mapping that makes measurement possible at all**: without it an
/// answer is a fact about a question and nothing more.
/// <para>
/// A mapping table rather than a column on the item, which is what makes re-targeting cheap — when
/// a real framework replaces the placeholders, this is an UPDATE and every historical observation
/// is regenerated from the immutable responses. The recompute line paying for itself (§20.5).
/// </para>
/// </summary>
public class ItemTargetMapping
{
    public Guid Id { get; set; }

    public Guid ItemId { get; set; }
    public Item? Item { get; set; }

    public Guid TargetId { get; set; }
    public LearningTarget? Target { get; set; }

    /// <summary>
    /// How much of this item is about this target. An item testing two things splits its weight; an
    /// item that merely touches a target carries a small one.
    /// </summary>
    public decimal Emphasis { get; set; } = 1.0m;

    /// <summary>
    /// The one target this item is mainly about. Exactly one per item, enforced by a filtered unique
    /// index — reporting needs a single answer to "what is this question for".
    /// </summary>
    public bool IsPrimary { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// node × target × emphasis. Makes "what does this lesson actually teach" machine-readable, which
/// is what coverage reporting reads and what an admin edits when the answer is wrong.
/// </summary>
public class NodeTargetMapping
{
    public Guid Id { get; set; }

    public Guid CurriculumVersionId { get; set; }

    /// <summary>A <c>CurriculumNode</c> id. Preserved from the legacy tree, so this resolves to a lesson today.</summary>
    public Guid NodeId { get; set; }

    public Guid TargetId { get; set; }
    public LearningTarget? Target { get; set; }

    public decimal Emphasis { get; set; } = 1.0m;

    public DateTime CreatedAtUtc { get; set; }
}
