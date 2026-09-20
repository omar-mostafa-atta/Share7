using Share7.Domain.Evidence;
using Share7.Domain.Measurement;

namespace Share7.Application.Measurement.Models;

/// <summary>
/// What is known about one learner and one learning target.
/// <para>
/// **There is no field here that carries an estimate on its own.** Every reader gets the interval
/// and the counts with the number, or gets <see cref="MasteryState.Insufficient"/> and no number at
/// all — which is the DTO-level enforcement of the rule in
/// <c>Docs/EducationalArchitecture.md</c> §12.5.4, put in the type system rather than in a review
/// checklist.
/// </para>
/// </summary>
public sealed record TargetMeasurementDto
{
    public required Guid TargetId { get; init; }

    /// <summary>The claim itself, in the reader's language. "Can order fractions with unlike denominators."</summary>
    public required string Statement { get; init; }

    /// <summary>
    /// True when this target is a lesson standing in for a real one. Surfaced rather than hidden:
    /// a reader is entitled to know that "Lesson 4" is not a competency somebody authored, and
    /// nothing may roll these up into a subject-level claim.
    /// </summary>
    public required bool IsPlaceholder { get; init; }

    public required MasteryState State { get; init; }

    /// <summary>
    /// Null when <see cref="State"/> is <see cref="MasteryState.Insufficient"/>. Not zero — the
    /// difference between "we do not know" and "they scored nothing" is the difference between an
    /// honest report and a defamatory one.
    /// </summary>
    public decimal? Estimate { get; init; }

    public decimal? IntervalLow { get; init; }
    public decimal? IntervalHigh { get; init; }

    /// <summary>How many observations back this. Always present, including when the answer is none.</summary>
    public required int ObservationCount { get; init; }

    /// <summary>How many of those were collected under assessment conditions.</summary>
    public required int AssessmentCount { get; init; }

    /// <summary>How many more admitted observations are needed before anything can be said.</summary>
    public required int ObservationsUntilReportable { get; init; }

    public DateTime? LastObservedAtUtc { get; init; }

    /// <summary>The rule version that produced <see cref="State"/>, so the verdict can be explained.</summary>
    public string? RuleKey { get; init; }
    public int? RuleVersion { get; init; }
}

/// <summary>One item's quality picture, for the admin surface.</summary>
public sealed record ItemQualityDto
{
    public required Guid ItemId { get; init; }
    public required Guid ItemVersionId { get; init; }
    public required int VersionNumber { get; init; }
    public required string SourceKey { get; init; }
    public required bool IsAnchor { get; init; }

    /// <summary>The stem, in whichever language was asked for. Null when that language has no rendering.</summary>
    public string? Stem { get; init; }

    public string? NodeTitle { get; init; }
    public Guid? NodeId { get; init; }

    public required int NTotal { get; init; }
    public required int NFirstEncounter { get; init; }

    /// <summary>
    /// Proportion correct on first, controlled encounters. **Higher means easier.** Null below the
    /// reporting floor, which is a statement rather than a gap.
    /// </summary>
    public double? Facility { get; init; }

    public double? MeanElapsedMs { get; init; }

    /// <summary>
    /// How often each choice was chosen, including the right one. The distractor breakdown is the
    /// first genuinely useful thing this platform can hand a teacher.
    /// </summary>
    public required IReadOnlyList<ChoiceShareDto> Choices { get; init; }

    /// <summary>What, if anything, is wrong with this item. Empty when nothing is.</summary>
    public required IReadOnlyList<string> Flags { get; init; }
}

public sealed record ChoiceShareDto
{
    public required Guid ChoiceId { get; init; }
    public string? Text { get; init; }
    public required bool IsCorrect { get; init; }
    public required int Count { get; init; }
    public required double Share { get; init; }
}

/// <summary>Flags the quality surface raises, as keys the client localizes.</summary>
public static class ItemQualityFlags
{
    /// <summary>Below the reporting floor. Not a fault — a statement that nothing can be said yet.</summary>
    public const string InsufficientData = "insufficient_data";

    /// <summary>Almost everybody gets it right. It is not discriminating; it may be a freebie.</summary>
    public const string TooEasy = "too_easy";

    /// <summary>Almost nobody gets it right. Often a real difficulty; often a wrong answer key.</summary>
    public const string TooHard = "too_hard";

    /// <summary>
    /// A wrong answer is chosen more often than the right one. **The strongest signal of a
    /// mis-keyed item there is** — and the reason the exclusion machinery in
    /// <c>ObservationExclusionReason</c> exists.
    /// </summary>
    public const string DistractorBeatsKey = "distractor_beats_key";

    /// <summary>A choice nobody ever picks. The item is effectively two-option.</summary>
    public const string DeadDistractor = "dead_distractor";

    /// <summary>Answered far faster than its siblings — often guessed, sometimes memorised.</summary>
    public const string AnsweredTooFast = "answered_too_fast";

    /// <summary>Mapped to no learning target, so no answer to it can ever be measured.</summary>
    public const string Unmapped = "unmapped";
}

/// <summary>The headline numbers for the admin quality surface.</summary>
public sealed record ContentQualitySummaryDto
{
    public required int Items { get; init; }
    public required int ItemsWithResponses { get; init; }
    public required int ItemsAboveReportingFloor { get; init; }
    public required int ItemsUnmapped { get; init; }
    public required int AnchorItems { get; init; }

    public required int Responses { get; init; }
    public required int Observations { get; init; }
    public required int ObservationsExcluded { get; init; }
    public required long PendingResponses { get; init; }

    public required IReadOnlyDictionary<EvidenceStrength, int> ObservationsByStrength { get; init; }
    public required IReadOnlyDictionary<string, int> FlagCounts { get; init; }

    public required int PlaceholderTargets { get; init; }
    public required int AuthoredTargets { get; init; }
    public required int ReportingFloor { get; init; }

    /// <summary>
    /// How the generic curriculum node projection compares with the typed tree it is derived from.
    /// <para>
    /// Surfaced because the projection is invisible in the authoring UI — an admin adds a chapter
    /// through the old tree editor and the node table follows silently. Silent is fine until it
    /// stops being true, and then nothing in the console would say so.
    /// </para>
    /// </summary>
    public required CurriculumProjectionStatusDto CurriculumProjection { get; init; }
}

/// <summary>Whether the derived node tree still agrees with the typed tables it is built from.</summary>
public sealed record CurriculumProjectionStatusDto
{
    public required int LiveNodes { get; init; }
    public required int RetiredNodes { get; init; }
    public required int LegacyRows { get; init; }

    /// <summary>The version label these nodes belong to — "as-migrated" until a real publish exists.</summary>
    public required string VersionLabel { get; init; }

    /// <summary>
    /// False while the typed tables are still the source of truth. It flips when the last reader
    /// has moved off them, and not before — the strangler fig does not cut the host down until
    /// nothing is standing on it.
    /// </summary>
    public required bool IsAuthoritative { get; init; }

    public required IReadOnlyDictionary<string, int> NodesByKind { get; init; }

    /// <summary>Legacy rows with no node, which should always be zero.</summary>
    public required int Missing { get; init; }
}
