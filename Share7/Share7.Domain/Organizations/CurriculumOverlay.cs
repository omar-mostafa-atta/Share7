using Share7.Domain.Structure;

namespace Share7.Domain.Organizations;

/// <summary>
/// An organization's edits to a curriculum it does not own, held as a diff and resolved at read
/// time.
///
/// <para><b>Overlay, not mutation — one of the four mechanisms that keep official curricula
/// uncorruptible</b> (§17.2). A school that reorders the national syllabus, hides a chapter it
/// covers elsewhere and inserts two of its own lessons has made none of those changes to the
/// ministry's version: it has written rows here, and only its own cohorts read them.</para>
///
/// <para><b>Everything a teacher wants to do is one of five verbs</b> — hide, reorder, insert,
/// substitute, repace — and all five can be applied by walking the official tree once. A closed set
/// is what makes this a diff rather than a second curriculum, and it is the reason "a teacher
/// customised the national curriculum" never becomes "there are two national curricula"
/// (§7.4).</para>
///
/// <para><b>Evidence names the official version, not the overlay.</b> A learner who answered a
/// question in a substituted lesson produced evidence against the target that lesson teaches; the
/// overlay changed what they were shown and in what order, not what the answer means. That is why
/// this table touches nothing in the evidence layer.</para>
/// </summary>
public class CurriculumOverlay
{
    public Guid Id { get; set; }

    /// <summary>Stable and human-readable, for the same reason every other key here is.</summary>
    public string OverlayKey { get; set; } = string.Empty;

    /// <summary>The organization that owns it. Overlays are org property, never platform property.</summary>
    public Guid OrgId { get; set; }
    public Organization? Org { get; set; }

    /// <summary>The published version this is a diff against.</summary>
    public Guid CurriculumVersionId { get; set; }
    public CurriculumVersion? CurriculumVersion { get; set; }

    public string Name { get; set; } = string.Empty;

    public OverlayStatus Status { get; set; }

    /// <summary>
    /// Why this overlay exists, in the org's own words. Shown wherever a learner or a parent sees
    /// a tree that differs from the published one — a curriculum that does not match the textbook
    /// is alarming unless somebody says who changed it and why.
    /// </summary>
    public string? SourceNote { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public ICollection<CurriculumOverlayEdit> Edits { get; set; } = new List<CurriculumOverlayEdit>();
}

/// <summary>
/// One edit in an overlay.
///
/// <para>Deliberately flat rather than a shape per verb: an overlay is applied by a single pass
/// that switches on <see cref="Kind"/>, and five tables would make that pass five joins for no
/// gain. The unused columns for a given kind are the cost, and they are cheap.</para>
/// </summary>
public class CurriculumOverlayEdit
{
    public Guid Id { get; set; }

    public Guid OverlayId { get; set; }
    public CurriculumOverlay? Overlay { get; set; }

    public OverlayEditKind Kind { get; set; }

    /// <summary>
    /// The official node this edit acts on. For <see cref="OverlayEditKind.Insert"/> it is the
    /// parent the new node goes under.
    /// </summary>
    public Guid TargetNodeId { get; set; }

    /// <summary>
    /// The node shown instead, for <see cref="OverlayEditKind.Substitute"/>, or the node inserted,
    /// for <see cref="OverlayEditKind.Insert"/>. Null for hide, reorder and repace.
    /// </summary>
    public Guid? ReplacementNodeId { get; set; }

    /// <summary>New position among siblings, 1-based. Reorder and insert only.</summary>
    public int? NewOrder { get; set; }

    /// <summary>When the org intends this to be taught. Repace only; it changes no structure.</summary>
    public DateTime? ScheduledFromUtc { get; set; }
    public DateTime? ScheduledToUtc { get; set; }

    /// <summary>Order in which edits are applied. Two edits on one node have to resolve predictably.</summary>
    public int ApplyOrder { get; set; }

    public string? Note { get; set; }
}
