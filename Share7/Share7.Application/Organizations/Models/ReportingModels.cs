using Share7.Domain.Measurement;
using Share7.Domain.Organizations;

namespace Share7.Application.Organizations.Models;

/// <summary>
/// Why a cell in a report carries no number.
/// <para>
/// <b>Suppression is a stated reason, never a blank.</b> A report that silently omits small cohorts
/// teaches its reader that blank means zero, and a head teacher acting on "0% mastered" for a class
/// of four is acting on a fabrication (§19.2).
/// </para>
/// </summary>
public enum SuppressionReason
{
    /// <summary>Nothing suppressed; the figure is reportable.</summary>
    None = 0,

    /// <summary>
    /// Fewer learners than the small-cell floor. Both statistically meaningless and
    /// de-anonymising: in a cohort of four, "one learner has not met this" names a child.
    /// </summary>
    SmallCell = 1,

    /// <summary>The cohort exists and nobody in it has produced admissible evidence here yet.</summary>
    NoEvidence = 2,

    /// <summary>The viewer's scope covers none of these learners' enrolments (§9.2).</summary>
    OutOfScope = 3
}

/// <summary>
/// How a cohort stands on one learning target.
///
/// <para><b>A distribution, not an average.</b> "This class averages 64%" hides the shape that
/// matters — four children who have not started and twenty who are fine is a different teaching
/// problem from twenty-four who are all halfway, and the average is identical.</para>
/// </summary>
public sealed record CohortTargetRowDto
{
    public required Guid TargetId { get; init; }
    public required string Statement { get; init; }
    public required bool IsPlaceholder { get; init; }

    /// <summary>Learners in the cohort whose evidence this row could have covered.</summary>
    public required int LearnerCount { get; init; }

    /// <summary>Of those, how many have enough admitted evidence for a verdict at all.</summary>
    public required int ReportableCount { get; init; }

    public required int MasteredCount { get; init; }
    public required int DevelopingCount { get; init; }
    public required int NotMetCount { get; init; }
    public required int InsufficientCount { get; init; }

    /// <summary>
    /// Null whenever <see cref="Suppression"/> is not <see cref="SuppressionReason.None"/>. Never
    /// zero in that case — the whole point is that the number does not exist.
    /// </summary>
    public decimal? MasteredShare { get; init; }

    public required SuppressionReason Suppression { get; init; }

    public DateTime? LastObservedAtUtc { get; init; }
}

/// <summary>
/// What a cohort's own evidence says, as a teacher would want to read it.
///
/// <para><b>The most valuable row is usually a teaching signal, not a learning one.</b> When a
/// whole cohort fails the same item, the likeliest explanation is not twenty-eight simultaneous
/// misconceptions — see <see cref="StrugglingTargets"/>, which is ordered to put that case
/// first (§19.2).</para>
/// </summary>
public sealed record CohortReportDto
{
    public required Guid CohortId { get; init; }
    public required string CohortName { get; init; }
    public required string AcademicPeriod { get; init; }
    public required Guid OrgId { get; init; }
    public required string OrgName { get; init; }

    public required int LearnerCount { get; init; }

    /// <summary>Learners with at least one admitted observation. The denominator that is honest.</summary>
    public required int ActiveLearnerCount { get; init; }

    /// <summary>
    /// The floor below which no share is reported, carried in the payload so a reader does not have
    /// to know it — and so it can be raised without every client changing.
    /// </summary>
    public required int SmallCellThreshold { get; init; }

    /// <summary>Set when the whole report is suppressed, which is the ordinary state of a new cohort.</summary>
    public required SuppressionReason Suppression { get; init; }

    public required IReadOnlyList<CohortTargetRowDto> Targets { get; init; }

    /// <summary>
    /// The targets this cohort is doing worst on, worst first, among those with enough evidence to
    /// say so. Empty rather than padded when there are not enough.
    /// </summary>
    public required IReadOnlyList<CohortTargetRowDto> StrugglingTargets { get; init; }

    /// <summary>
    /// Total admitted observations behind the whole report. Every aggregate here carries its N,
    /// and this is the outermost one.
    /// </summary>
    public required int ObservationCount { get; init; }
}

/// <summary>
/// One learner as a teacher may see them: verdicts and counts, never raw sessions.
/// <para>
/// <b>What is missing is the point.</b> There is no timestamp of when the child played, no session
/// length, no device, no score and no coins. A teacher seeing error patterns is pedagogy; a teacher
/// seeing that a child was answering questions at eleven at night is surveillance, and the line is
/// drawn by what this record can hold (§9.5).
/// </para>
/// </summary>
public sealed record CohortLearnerRowDto
{
    public required Guid LearnerId { get; init; }
    public required string UserName { get; init; }
    public string? FullName { get; init; }

    /// <summary>Targets with a verdict of any kind. The evidence base, not an achievement.</summary>
    public required int ReportableTargetCount { get; init; }

    public required int MasteredCount { get; init; }
    public required int DevelopingCount { get; init; }
    public required int NotMetCount { get; init; }

    public required int ObservationCount { get; init; }

    /// <summary>
    /// The most recent admitted observation, to the day. <b>Deliberately a date rather than a
    /// timestamp</b> — "has not worked on this for two weeks" is a teaching fact, "was working at
    /// 23:41" is not the school's business.
    /// </summary>
    public DateOnly? LastActiveOn { get; init; }
}

/// <summary>
/// What a guardian may see about one learner.
///
/// <para><b>Never a number without a meaning</b> (§18.2). There is no overall percentage on this
/// record and no field one could be written into: "Ahmed is 72% at mathematics" is uninterpretable
/// and will be read as a school grade, where "has shown he can do 8 of the 11 things this term
/// covers, and fractions with unlike denominators is the gap" is actionable and true.</para>
///
/// <para><b>Never a prediction that fails the sufficiency gate.</b> A parent acts on an exam
/// projection — tutoring, money, pressure — so the projection is fetched through the same gated
/// service the learner's own screen uses, and is absent rather than hedged.</para>
/// </summary>
public sealed record GuardianReportDto
{
    public required Guid LearnerId { get; init; }
    public required string LearnerName { get; init; }

    /// <summary>What the guardian's link actually permits, echoed so a portal need not guess.</summary>
    public required GuardianConsentScope ConsentScope { get; init; }

    /// <summary>Claims with enough evidence to say the learner can do them.</summary>
    public required IReadOnlyList<PlainClaimDto> Strengths { get; init; }

    /// <summary>Claims with enough evidence to say they cannot yet. The actionable half.</summary>
    public required IReadOnlyList<PlainClaimDto> Gaps { get; init; }

    /// <summary>
    /// Claims the platform has not seen enough of to judge. Shown, not hidden: a parent is entitled
    /// to know the difference between "not yet" and "we have not looked".
    /// </summary>
    public required int NotYetObservedCount { get; init; }

    public required int ObservationCount { get; init; }

    public DateOnly? LastActiveOn { get; init; }
}

/// <summary>
/// One claim about a learner, in language a parent reads rather than a decimal they interpret.
/// </summary>
public sealed record PlainClaimDto
{
    public required Guid TargetId { get; init; }
    public required string Statement { get; init; }
    public required MasteryState State { get; init; }

    /// <summary>
    /// How many answers stand behind it. <b>Carried instead of the estimate</b>: it is the part a
    /// parent can actually reason about, and it cannot be mistaken for a grade.
    /// </summary>
    public required int ObservationCount { get; init; }
}
