namespace Share7.Domain.Competency;

/// <summary>
/// How two learning targets relate. **Typed, not generic** — an untyped "related to" edge cannot
/// answer a question, and a generic graph engine is exactly what this design refuses to build
/// (<c>Docs/EducationalArchitecture.md</c> §2.3).
/// </summary>
public enum LearningTargetEdgeKind
{
    /// <summary>
    /// The source is a part of the destination. Aggregation walks these upward, and only these:
    /// "can add fractions" is a component of "can do fraction arithmetic".
    /// </summary>
    ComponentOf = 0,

    /// <summary>
    /// The source must be in place before the destination is teachable. Drives remediation and
    /// sequencing, never aggregation — being ready for something is not partly knowing it.
    /// </summary>
    PrerequisiteOf = 1,

    /// <summary>
    /// The destination replaces the source in a later framework revision. Measurements on the old
    /// target stay readable and carry forward to the new one where the alignment allows.
    /// </summary>
    SupersededBy = 2
}

/// <summary>How far a target has been through human review.</summary>
public enum TargetReviewState
{
    /// <summary>Machine-generated or freshly authored. Placeholders are always here.</summary>
    Unreviewed = 0,

    /// <summary>A subject specialist has confirmed the statement is a real, measurable claim.</summary>
    Reviewed = 1,

    /// <summary>Kept for history; no longer mapped to new content.</summary>
    Deprecated = 2
}

/// <summary>
/// How closely a target in one framework matches a target in another. Carries a strength because
/// cross-curriculum equivalence is rarely exact, and pretending it is would let an IGCSE
/// measurement masquerade as a national-curriculum one.
/// </summary>
public enum AlignmentStrength
{
    /// <summary>Same claim, different words.</summary>
    Exact = 0,

    /// <summary>The destination covers this and more.</summary>
    Broader = 1,

    /// <summary>The destination covers part of this.</summary>
    Narrower = 2,

    /// <summary>They overlap. Usable for navigation; **never** for transferring a measurement.</summary>
    Partial = 3
}
