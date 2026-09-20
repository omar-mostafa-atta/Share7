using Share7.Domain.LookUps;

namespace Share7.Domain.Competency;

/// <summary>
/// A published vocabulary of claims that can be made about a learner. A framework is versioned as a
/// whole because a claim only means something inside the set it was written for.
/// </summary>
public class CompetencyFramework
{
    public Guid Id { get; set; }

    /// <summary>Stable and human-readable — <c>share7.placeholder</c> for the bootstrap framework.</summary>
    public string FrameworkKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Who published it. Null for Share7's own frameworks.</summary>
    public Guid? AuthorityId { get; set; }

    public string VersionLabel { get; set; } = string.Empty;

    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<LearningTarget> Targets { get; set; } = new List<LearningTarget>();
}

/// <summary>
/// The measurable unit. **The only thing proficiency can ever be about.**
/// <para>
/// Not a lesson, not a chapter, not a subject. A lesson is a place in a plan of instruction and a
/// subject is a label on a timetable; neither is a claim that can be true or false about a child.
/// "Can order fractions with unlike denominators" is. Everything in the measurement layer is keyed
/// by a target for exactly this reason — see <c>Docs/EducationalArchitecture.md</c> §2.4.
/// </para>
/// </summary>
public class LearningTarget
{
    public Guid Id { get; set; }

    public Guid FrameworkId { get; set; }
    public CompetencyFramework? Framework { get; set; }

    /// <summary>Stable within the framework. Unique with <see cref="FrameworkId"/>.</summary>
    public string TargetKey { get; set; } = string.Empty;

    /// <summary>
    /// What kind of claim this is — <c>skill</c>, <c>concept</c>, <c>procedure</c>,
    /// <c>lesson_placeholder</c>. Data rather than an enum, because frameworks disagree about the
    /// taxonomy and a new one must not need a migration.
    /// </summary>
    public string TargetKindKey { get; set; } = TargetKinds.Skill;

    /// <summary>
    /// A lesson wearing a target's clothes, minted by the bootstrap so that measurement can start
    /// before a real framework exists.
    /// <para>
    /// **Excluded from cross-target aggregation and from every exam projection**, and that exclusion
    /// is the reason it is safe to have these at all. Rolling placeholders up would produce a
    /// completion percentage dressed as a proficiency claim — the precise fabrication this
    /// architecture exists to prevent (§20.5). Per-lesson measurement over a placeholder is honest:
    /// it says no more than "on the items in this lesson", which is exactly what it measured.
    /// </para>
    /// </summary>
    public bool IsPlaceholder { get; set; }

    public TargetReviewState ReviewState { get; set; } = TargetReviewState.Unreviewed;

    /// <summary>
    /// Nominal difficulty band, for blueprint construction. Null until somebody who knows has said.
    /// **Never inferred from how many children got it wrong** — that is the item's property, not the
    /// target's.
    /// </summary>
    public int? DifficultyBand { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }

    public ICollection<LearningTargetTranslation> Translations { get; set; } = new List<LearningTargetTranslation>();
}

/// <summary>The statement itself, per language. Same stable-id-per-language pattern as the tree.</summary>
public class LearningTargetTranslation
{
    public Guid TargetId { get; set; }
    public LearningTarget? Target { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    /// <summary>"Can order fractions with unlike denominators." A claim, phrased as one.</summary>
    public string Statement { get; set; } = string.Empty;
}

/// <summary>Target kind keys the platform itself creates. Others are authored freely.</summary>
public static class TargetKinds
{
    public const string Skill = "skill";
    public const string Concept = "concept";
    public const string Procedure = "procedure";

    /// <summary>One target standing in for one lesson, until real targets are authored. §20.5.</summary>
    public const string LessonPlaceholder = "lesson_placeholder";
}
