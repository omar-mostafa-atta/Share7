using Share7.Domain.LookUps;

namespace Share7.Domain.Structure;

/// <summary>
/// Who may publish a curriculum, and how far what they publish is trusted.
/// <para>
/// The trust tier is not decoration: an item bank owned by a ministry and one owned by a tutoring
/// centre produce evidence with different ceilings, and the difference has to live somewhere that
/// is not a code branch.
/// </para>
/// </summary>
public class CurriculumAuthority
{
    public Guid Id { get; set; }
    public string AuthorityKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2, or null for an international body.</summary>
    public string? CountryCode { get; set; }

    /// <summary>0 = a private author, 1 = a recognised publisher, 2 = a national authority.</summary>
    public int TrustTier { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// The stable identity of a plan of instruction, across its revisions. "Egyptian National
/// Curriculum" is a <see cref="Curriculum"/>; "as published for 2026/27" is a
/// <see cref="CurriculumVersion"/>.
/// </summary>
public class Curriculum
{
    public Guid Id { get; set; }
    public string CurriculumKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid? AuthorityId { get; set; }
    public CurriculumAuthority? Authority { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<CurriculumVersion> Versions { get; set; } = new List<CurriculumVersion>();
}

/// <summary>
/// One immutable published revision of a curriculum. **This is what evidence names.**
/// <para>
/// When the ministry rewrites the syllabus, a new version is published beside the old one rather
/// than over it. Learners mid-year stay enrolled in the version they started; history keeps
/// resolving to the structure it was collected under; nobody's past becomes unreadable because the
/// present changed (§23 scenario 3).
/// </para>
/// </summary>
public class CurriculumVersion
{
    public Guid Id { get; set; }

    public Guid CurriculumId { get; set; }
    public Curriculum? Curriculum { get; set; }

    public string VersionLabel { get; set; } = string.Empty;

    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime? PublishedAtUtc { get; set; }

    /// <summary>
    /// Whether this version is the live source of truth for its structure, or a projection
    /// maintained alongside one. The migrated Egyptian tree starts as a projection of the legacy
    /// typed tables and becomes authoritative only when every reader has moved off them.
    /// </summary>
    public bool IsAuthoritative { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<CurriculumNodeKind> NodeKinds { get; set; } = new List<CurriculumNodeKind>();
}

/// <summary>
/// Declares the tree shape for one curriculum version. **The replacement for five hardcoded
/// tables.**
/// <para>
/// Egypt declares grade then term then subject then chapter then lesson. IGCSE declares subject,
/// paper, topic, subtopic. A country with no terms and thirteen years of schooling declares
/// whatever it has. None of them need a migration, a new table or a line of code, because the shape
/// is data — which is the entire reason the brief's warning about assuming one hierarchy could be
/// honoured (§7.2).
/// </para>
/// </summary>
public class CurriculumNodeKind
{
    public Guid Id { get; set; }

    public Guid CurriculumVersionId { get; set; }
    public CurriculumVersion? CurriculumVersion { get; set; }

    /// <summary>grade, term, subject, chapter, lesson, paper, topic — whatever the curriculum has.</summary>
    public string KindKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>0-based depth this kind occupies. A sanity check on the tree, not a constraint on it.</summary>
    public int Depth { get; set; }

    /// <summary>Which kind may parent this one. Null for the root kind.</summary>
    public string? ParentKindKey { get; set; }

    /// <summary>
    /// Whether a node of this kind can be played. **Today only lessons are**, and the client's
    /// hardcoded six-level path is the one place that assumption is still baked in — §15.1.
    /// </summary>
    public bool IsPlayable { get; set; }

    public int Order { get; set; }
}

/// <summary>
/// One recursive position in a curriculum tree. Absorbs <c>Grade</c> / <c>Term</c> /
/// <c>Subject</c> / <c>Chapter</c> / <c>Lesson</c>.
/// <para>
/// <b>Today these rows are a derived projection of the legacy typed tables, preserving their exact
/// GUIDs.</b> That is a deliberate deviation from the migration described in §20.3, which would
/// have moved the data and projected the old shape back. Preserving the ids and maintaining the
/// projection alongside means new code reads nodes generically, old code keeps reading the typed
/// tables, and the two cannot disagree because they are the same identifiers — a strangler fig
/// rather than a transplant, on a product that is live. The projection becomes authoritative when
/// the last typed reader is gone, and <see cref="CurriculumVersion.IsAuthoritative"/> is the flag
/// that says so.
/// </para>
/// </summary>
public class CurriculumNode
{
    /// <summary>**Preserved from the legacy row.** A lesson node's id is that lesson's id.</summary>
    public Guid Id { get; set; }

    public Guid CurriculumVersionId { get; set; }
    public CurriculumVersion? CurriculumVersion { get; set; }

    public Guid? ParentNodeId { get; set; }
    public CurriculumNode? Parent { get; set; }

    public Guid NodeKindId { get; set; }
    public CurriculumNodeKind? NodeKind { get; set; }

    /// <summary>Denormalised from <see cref="NodeKind"/> so a tree read needs no join to filter.</summary>
    public string KindKey { get; set; } = string.Empty;

    /// <summary>Position among siblings, 1-based. Drives the unlock chain, as Order always did.</summary>
    public int Order { get; set; }

    public int Depth { get; set; }

    /// <summary>
    /// Materialised ancestry, slash-separated ids. Makes "everything under this subject" one
    /// indexed prefix scan instead of a recursive CTE per read, which is what every report wants
    /// and no report wants to pay for.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    public bool IsPlayable { get; set; }

    /// <summary>
    /// Which legacy table this node was projected from, while the projection is still derived.
    /// Drops out when the typed tables do.
    /// </summary>
    public string? LegacySource { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }

    public ICollection<CurriculumNodeTranslation> Translations { get; set; } = new List<CurriculumNodeTranslation>();
}

/// <summary>
/// Node display text per language.
/// <para>
/// Derived today from <c>GradeTranslation</c> / <c>TermTranslation</c> and the rest, and
/// deliberately duplicated rather than read through a five-way union: the whole point of a generic
/// node is that a reader does not have to know which of five tables a title lives in.
/// </para>
/// </summary>
public class CurriculumNodeTranslation
{
    public Guid NodeId { get; set; }
    public CurriculumNode? Node { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public string Title { get; set; } = string.Empty;
}
