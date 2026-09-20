namespace Share7.Domain.Measurement;

/// <summary>What one observation says happened, against one target.</summary>
public enum ObservationOutcome
{
    Incorrect = 0,
    Correct = 1,

    /// <summary>Partially right, where the item kind allows it. No current kind does.</summary>
    Partial = 2,

    /// <summary>The item was never reached. **Not the same as wrong** — see the note on weighting.</summary>
    NoResponse = 3
}

/// <summary>
/// Why an observation stopped counting. **Annotated, never deleted** — the child did answer, and
/// that is a fact; what changes is whether it is allowed to inform a measurement.
/// <para>
/// This pair is what makes a mis-keyed item survivable. When a question turns out to have been
/// marked wrong for fifty thousand children, you do not delete the responses: you exclude the
/// observations, stamp the reason and recompute. The numbers move, the history does not, and the
/// audit trail explains exactly why. A system that deletes here has destroyed its own evidence that
/// it made a mistake — <c>Docs/EducationalArchitecture.md</c> §12.3.
/// </para>
/// </summary>
public enum ObservationExclusionReason
{
    /// <summary>The answer key was wrong. The commonest reason, and the one this exists for.</summary>
    MisKeyedItem = 1,

    /// <summary>The contract that admitted it was withdrawn — the interaction did not mean what it claimed.</summary>
    InvalidatedContract = 2,

    /// <summary>Cheating, automation, or a response pattern that is not a person answering.</summary>
    IntegrityFlag = 3,

    /// <summary>Someone else was holding the device. Includes a co-op answer credited to one child.</summary>
    Misattributed = 4,

    /// <summary>Consent for this use was withdrawn.</summary>
    WithdrawnConsent = 5,

    /// <summary>The item was remapped to a different target and this observation is about the old one.</summary>
    Remapped = 6
}

/// <summary>
/// A judgement, applying a stated rule to a measurement. **Never a number pretending to be a
/// verdict**, and never a verdict without a rule that can be read.
/// </summary>
public enum MasteryState
{
    /// <summary>
    /// **Not a failure — an absence.** There is not enough evidence to say anything, and saying so
    /// is the honest answer. A first-class state rather than a zero, because reporting "0%" for a
    /// child who has answered four questions is a fabrication with a number attached.
    /// </summary>
    Insufficient = 0,

    /// <summary>Enough evidence, and it says the learner is not there yet.</summary>
    NotMet = 1,

    /// <summary>Enough evidence, and it says the learner is on the way.</summary>
    Developing = 2,

    /// <summary>Enough evidence, and the interval clears the bar even at its low end.</summary>
    Mastered = 3
}

/// <summary>
/// The method that produced a measurement. A string because methods coexist rather than replace one
/// another: <c>irt_2pl_v1</c> will shadow <c>ctt_v1</c> for a long time before anything switches,
/// and a measurement whose method is not readable is not interpretable (§5.3).
/// </summary>
public static class MeasurementMethods
{
    /// <summary>
    /// Classical test theory: proportion correct on first, controlled encounters, with a Wilson
    /// score interval. Honest at every sample size, needs no calibration, and is what the data
    /// currently supports.
    /// </summary>
    public const string ClassicalV1 = "ctt_v1";
}
