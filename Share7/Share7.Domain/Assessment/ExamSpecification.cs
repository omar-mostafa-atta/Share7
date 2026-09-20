using Share7.Domain.Structure;

namespace Share7.Domain.Assessment;

/// <summary>
/// An examination that exists in the world and that Share7 does not administer — a national
/// certificate, a board paper, a school's own end-of-year.
/// <para>
/// It belongs to an <see cref="CurriculumAuthority"/> and blueprints against a competency
/// framework rather than a curriculum, and that single choice is what makes §6.6 work: a learner on
/// the national curriculum and a learner at a tutoring centre project against the same exam,
/// because both of their evidence sets attach to framework targets rather than to a position in
/// somebody's tree. In a model where the exam is a node in one curriculum, this is not a hard
/// problem — it is an impossible one.
/// </para>
/// </summary>
public class ExamSpecification
{
    public Guid Id { get; set; }

    public string SpecificationKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Who sets the paper. **Not decoration**: an exam attributed to a ministry carries the
    /// ministry's authority to every parent who reads the report, so a specification Share7 derived
    /// for itself belongs to Share7's own authority and says so.
    /// </summary>
    public Guid? AuthorityId { get; set; }
    public CurriculumAuthority? Authority { get; set; }

    /// <summary>ISO 3166-1 alpha-2, or null for an international qualification.</summary>
    public string? CountryCode { get; set; }

    /// <summary>The subject as the examining body names it. Free text — bodies do not share a taxonomy.</summary>
    public string? SubjectLabel { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }

    public ICollection<ExamSpecificationVersion> Versions { get; set; } = new List<ExamSpecificationVersion>();
}

/// <summary>
/// One sitting's published specification: this exam, this year, this blueprint.
/// <para>
/// Versioned per sitting because papers change and a coverage figure computed against last year's
/// spec is a statement about last year. Every projection and every reported outcome names the
/// version, so calibration never pools two different papers into one sample.
/// </para>
/// </summary>
public class ExamSpecificationVersion
{
    public Guid Id { get; set; }

    public Guid ExamSpecificationId { get; set; }
    public ExamSpecification? ExamSpecification { get; set; }

    /// <summary>"2026 June", "2026/27". Unique within the specification.</summary>
    public string VersionLabel { get; set; } = string.Empty;

    /// <summary>
    /// What the paper examines. **The same type a teacher's test is checked against** — which is
    /// the unification that makes the whole of §6 affordable, because coverage arithmetic written
    /// once serves both.
    /// </summary>
    public Guid BlueprintId { get; set; }
    public AssessmentBlueprint? Blueprint { get; set; }

    /// <summary>When the paper is or was sat. Drives recency and the prompt to report an outcome.</summary>
    public DateOnly? SittingDate { get; set; }

    /// <summary>Marks available on the real paper, when the body publishes it.</summary>
    public int? MaxScore { get; set; }

    /// <summary>The mark that passes, when there is one. Never inferred.</summary>
    public int? PassingScore { get; set; }

    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<ReportedExamOutcome> ReportedOutcomes { get; set; } = new List<ReportedExamOutcome>();
}
