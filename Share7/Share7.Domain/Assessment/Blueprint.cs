using Share7.Domain.Competency;
using Share7.Domain.Evidence;

namespace Share7.Domain.Assessment;

/// <summary>
/// What a test is supposed to measure, stated so a machine can check it.
/// <para>
/// **This is the object the whole of §6 stands on**, and the reason it is separate from a form.
/// A form is one list of questions and supports exactly one question: "what did this learner sit?"
/// A blueprint says "eight items on target A at mixed difficulty, four on target B, none below
/// difficulty 2, twenty minutes, no retries", and from that you can generate many forms, check
/// whether a teacher's hand-built test is balanced, notice that a syllabus change has orphaned a
/// requirement, and — the commercially interesting one — compute whether a learner's evidence
/// covers the exam in the exam's own proportions without the learner having sat anything at all.
/// </para>
/// <para>
/// It blueprints against a <see cref="CompetencyFramework"/> rather than a curriculum, which is
/// what lets one exam be projected for learners on different curricula (§6.6). A blueprint that
/// named chapters would be unusable the moment a school reordered them.
/// </para>
/// </summary>
public class AssessmentBlueprint
{
    public Guid Id { get; set; }

    /// <summary>Stable and human-readable. Unique with <see cref="VersionNumber"/>.</summary>
    public string BlueprintKey { get; set; } = string.Empty;

    /// <summary>
    /// Immutable once published. A blueprint is a claim about what an exam contains, and editing
    /// one in place would silently change the meaning of every coverage figure ever computed from
    /// it. Revisions supersede.
    /// </summary>
    public int VersionNumber { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    /// <summary>The vocabulary its lines are written in. Lines must name targets from this framework.</summary>
    public Guid FrameworkId { get; set; }
    public CompetencyFramework? Framework { get; set; }

    /// <summary>How many items a form built from this should contain. Null when the lines say.</summary>
    public int? TotalItemCount { get; set; }

    /// <summary>The sitting's time limit, or null for untimed. Part of the conditions it demands.</summary>
    public int? TimeLimitMs { get; set; }

    /// <summary>
    /// Whether a sitting may let a learner re-answer within the administration. False for anything
    /// exam-like, and it is what decides whether the resulting evidence can reach
    /// <see cref="EvidenceStrength.Assessment"/>.
    /// </summary>
    public bool RetryPermitted { get; set; }

    /// <summary>
    /// The evidence class this blueprint's coverage arithmetic will admit. <see cref="EvidenceStrength.Assessment"/>
    /// for anything exam-facing — a coverage figure built from practice evidence would answer a
    /// different question than the one it appears to answer.
    /// </summary>
    public EvidenceStrength RequiredStrength { get; set; } = EvidenceStrength.Assessment;

    // --------------------------------------------------------------- sufficiency thresholds

    /// <summary>
    /// The share of blueprint weight that must be covered before a projection is attempted.
    /// **Stored on the blueprint rather than in code** because it is a property of the exam: a
    /// broad survey paper and a narrow specialist one do not deserve the same bar (§6.3).
    /// </summary>
    public decimal MinCoverageRatio { get; set; } = 0.70m;

    /// <summary>
    /// The floor no single area may fall below, however good the overall figure is.
    /// **This is the condition that matters.** An average of 0.75 hides a total blind spot in
    /// geometry, and the learner who trusts it walks into the exam having never been told.
    /// </summary>
    public decimal MinAreaCoverageRatio { get; set; } = 0.40m;

    /// <summary>Exam-like observations required overall before anything is projected.</summary>
    public int MinObservationsOverall { get; set; } = 20;

    /// <summary>Exam-like observations required in each area. A per-area floor, for the same reason.</summary>
    public int MinObservationsPerArea { get; set; } = 5;

    /// <summary>How old the median piece of evidence may be. Knowledge decays; a claim about a child should not outlive it.</summary>
    public int MaxMedianEvidenceAgeDays { get; set; } = 120;

    // ----------------------------------------------------------------------- lifecycle

    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Where this blueprint came from — a published exam syllabus, a subject lead, a structural
    /// derivation from the platform's own content. **Required reading before anybody trusts a
    /// coverage number**, because a blueprint nobody authored is an assumption with a schema.
    /// </summary>
    public string? SourceNote { get; set; }

    public ICollection<AssessmentBlueprintArea> Areas { get; set; } = new List<AssessmentBlueprintArea>();
}

/// <summary>
/// A named division of a blueprint — "Number", "Geometry", "Reading comprehension".
/// <para>
/// Exists so that the per-area floor in §6.3 is computable. Without areas, coverage is one
/// weighted average, and a weighted average is exactly the shape of reporting that lets a blind
/// spot pass unnoticed. With them, the honest sentence — "you are at 74% overall but you have
/// shown us nothing at all on statistics, which is a fifth of the paper" — becomes a query.
/// </para>
/// </summary>
public class AssessmentBlueprintArea
{
    public Guid Id { get; set; }

    public Guid BlueprintId { get; set; }
    public AssessmentBlueprint? Blueprint { get; set; }

    /// <summary>Stable within the blueprint. Unique with <see cref="BlueprintId"/>.</summary>
    public string AreaKey { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// This area's share of the paper. Normalised on read rather than validated to sum to one, so
    /// an author can write "40 marks, 30 marks, 30 marks" and mean it.
    /// </summary>
    public decimal Weight { get; set; } = 1.0m;

    public int Order { get; set; }

    public ICollection<AssessmentBlueprintLine> Lines { get; set; } = new List<AssessmentBlueprintLine>();
}

/// <summary>
/// One requirement: this target, this much of the area, at this difficulty, this many items.
/// <para>
/// A line naming a <see cref="LearningTarget.IsPlaceholder"/> target is accepted and marked, but
/// it can never support an exam claim — a lesson wearing a target's clothes cannot tell you what a
/// paper examines. The marking is surfaced rather than rejected because it is how a draft
/// blueprint gets built before a framework is authored, and refusing it would leave nowhere to
/// start.
/// </para>
/// </summary>
public class AssessmentBlueprintLine
{
    public Guid Id { get; set; }

    public Guid AreaId { get; set; }
    public AssessmentBlueprintArea? Area { get; set; }

    public Guid TargetId { get; set; }
    public LearningTarget? Target { get; set; }

    /// <summary>This line's share within its area. Normalised on read, like the area's own weight.</summary>
    public decimal Weight { get; set; } = 1.0m;

    /// <summary>How many items a generated form must draw for this line. Zero means "as many as fit".</summary>
    public int ItemCount { get; set; }

    /// <summary>Difficulty window for item selection, or null for any. Bands are 1..5 where 5 is hardest.</summary>
    public int? DifficultyBandLow { get; set; }
    public int? DifficultyBandHigh { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
