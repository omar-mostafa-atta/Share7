using Share7.Domain.Content;

namespace Share7.Domain.Measurement;

/// <summary>
/// What a population's answers say about an item, kept as **running aggregates**.
/// <para>
/// <b>Never recomputed solely from raw responses, and that is not an optimisation.</b> Erasure
/// requests are real, must work, and hard-delete a child's responses. Every item's difficulty was
/// computed from those rows; recomputing after a deletion would silently shift every item's
/// statistics and therefore every *other* child's measurements — one child exercising their rights
/// would move another child's reported proficiency. Aggregates are non-identifying and survive the
/// erasure of any individual's rows, so they are the thing that must outlive the learner.
/// <c>Docs/EducationalArchitecture.md</c> §12.4.
/// </para>
/// <para>
/// Period recomputation from surviving data is still possible and desirable; it is simply not the
/// only path, and it must never be the path taken right after a deletion.
/// </para>
/// </summary>
public class ItemStatistics
{
    /// <summary>Statistics are per **version**: a rewritten question is not the same question.</summary>
    public Guid ItemVersionId { get; set; }
    public ItemVersion? ItemVersion { get; set; }

    /// <summary>
    /// Which population these describe. <c>global</c> today; org-scoped banks and per-cohort
    /// calibration get their own rows rather than overwriting the global one.
    /// </summary>
    public string Population { get; set; } = ItemStatisticsPopulations.Global;

    public Guid ItemId { get; set; }

    // ---- counts ---------------------------------------------------------------------------

    public int NTotal { get; set; }
    public int NCorrect { get; set; }

    /// <summary>
    /// First, controlled encounters only. **This is the difficulty denominator** — a replay is a
    /// different question psychometrically, and pooling replays makes every item look easier than
    /// it is.
    /// </summary>
    public int NFirstEncounter { get; set; }
    public int NFirstEncounterCorrect { get; set; }

    // ---- timing ---------------------------------------------------------------------------

    /// <summary>Running sum, so the mean survives any individual row disappearing.</summary>
    public long SumElapsedMs { get; set; }
    public int NElapsed { get; set; }

    // ---- discrimination -------------------------------------------------------------------

    /// <summary>
    /// Running sums for a point-biserial-style correlation between getting this item right and
    /// doing well overall. Kept as numerator and denominator rather than as a coefficient, because
    /// a coefficient cannot be updated incrementally and this table may never be rebuilt from raw.
    /// **Left at zero until Phase 5** — computing it needs a total score per learner per form,
    /// which arrives with assessment administrations.
    /// </summary>
    public decimal DiscriminationNumerator { get; set; }
    public decimal DiscriminationDenominator { get; set; }

    /// <summary>
    /// How often each choice was picked, as JSON keyed by choice id. **Distractor analysis is the
    /// first genuinely useful thing this platform can tell a teacher**: a wrong answer that half
    /// the class chooses is a misconception with a name, not noise.
    /// </summary>
    public string? ChoiceFrequency { get; set; }

    /// <summary>
    /// The highest observation sequence folded in. Makes the fold idempotent without a separate
    /// checkpoint per item.
    /// </summary>
    public long LastObservationSequence { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    // ---- derived, never stored ------------------------------------------------------------

    /// <summary>
    /// Proportion correct on first encounters — the classical difficulty index, where **higher
    /// means easier**. Null below the reporting floor rather than a number nobody should read.
    /// </summary>
    public double? Facility => NFirstEncounter >= ItemStatisticsPopulations.MinimumForReporting
        ? (double)NFirstEncounterCorrect / NFirstEncounter
        : null;

    public double? MeanElapsedMs => NElapsed > 0 ? (double)SumElapsedMs / NElapsed : null;
}

/// <summary>Population keys and the floor below which an item statistic is not reported.</summary>
public static class ItemStatisticsPopulations
{
    public const string Global = "global";

    /// <summary>
    /// Below this many first encounters, a facility value is noise with a decimal point on it.
    /// Reported as "not enough data" rather than as a number — the same rule the mastery verdict
    /// applies to learners, applied to items.
    /// </summary>
    public const int MinimumForReporting = 30;
}
