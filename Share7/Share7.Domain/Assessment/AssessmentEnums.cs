namespace Share7.Domain.Assessment;

/// <summary>
/// What an administration was <i>for</i>. **A property of the sitting, never of the test** — the
/// same form is a diagnostic in September and a summative in June, and what changes is the claim
/// being made, not the questions (<c>Docs/EducationalArchitecture.md</c> §4.2).
/// </summary>
public enum AssessmentPurpose
{
    /// <summary>Where is this learner now, and what are the gaps? Placement, start of topic.</summary>
    Diagnostic = 0,

    /// <summary>Is this learner getting it, right now? Low stakes, inside gameplay.</summary>
    Formative = 1,

    /// <summary>What has this learner achieved over this unit? End of chapter or term.</summary>
    Summative = 2,

    /// <summary>How does this learner compare to a standard? Periodic and blueprint-matched.</summary>
    Benchmark = 3
}

/// <summary>Where a sitting has got to. A sitting is a small state machine and the states matter.</summary>
public enum AdministrationState
{
    /// <summary>Started; the learner is answering. The selector will hand out more items.</summary>
    InProgress = 0,

    /// <summary>Finished by the learner. Complete, and the evidence is exam-grade if the conditions were.</summary>
    Submitted = 1,

    /// <summary>
    /// Left unfinished. **Its responses still count** — answering eight questions and walking away
    /// is eight real answers. What does not count is the unreached remainder, which projects as
    /// <c>NoResponse</c> rather than wrong.
    /// </summary>
    Abandoned = 2,

    /// <summary>The time limit ran out server-side. Same evidence treatment as abandonment.</summary>
    Expired = 3
}

/// <summary>
/// Why a projection could not be made, which **is the output** when it fails.
/// <para>
/// The API does not degrade to a smaller number when evidence is thin; it says which of the four
/// claims in §6.1 does not hold, and the gap payload that goes with it is more actionable than any
/// score would have been. Ordering is not significant — these are reasons, not a scale.
/// </para>
/// </summary>
public enum ProjectionSufficiency
{
    /// <summary>Every gate cleared. Bands may be reported.</summary>
    Sufficient = 0,

    /// <summary>
    /// The learner's evidence does not cover the blueprint in the blueprint's own proportions.
    /// Either overall coverage is short or one area is a blind spot — and the second is the one
    /// that matters, because an average hides it (§6.3).
    /// </summary>
    InsufficientCoverage = 1,

    /// <summary>
    /// There is evidence, but not enough of it collected under exam-like conditions.
    /// Practice-with-retries does not generalize to a sitting, and this is the claim everybody
    /// gets wrong (§6.1.3).
    /// </summary>
    InsufficientConditions = 2,

    /// <summary>The evidence is real and exam-grade and too old to speak for the learner today.</summary>
    InsufficientRecency = 3,

    /// <summary>
    /// Everything about the learner is fine. What is missing is the mapping from proficiency to
    /// score, which **cannot be reasoned into existence** — it needs observed pairs of estimate
    /// and actual result. This is the honest state of every exam until §6.5 has run its course.
    /// </summary>
    Uncalibrated = 4
}

/// <summary>
/// How much confidence to put on a reported band. **A label and never a number**, because a
/// percentage attached to a confidence is a second false precision stacked on the first.
/// </summary>
public enum ConfidenceLabel
{
    Low = 0,
    Moderate = 1,
    High = 2
}

/// <summary>
/// How far a reported exam result has been checked. The calibration sample is stratified by this:
/// self-reported results are usable in aggregate with obvious caveats, school-supplied ones are
/// the real prize (§6.5).
/// </summary>
public enum OutcomeVerification
{
    /// <summary>A learner or guardian typed it in. Biased upward, and still worth having.</summary>
    SelfReported = 0,

    /// <summary>Supplied by a school under an agreement. Verified in the sense that matters.</summary>
    SchoolSupplied = 1,

    /// <summary>Came from the examining authority. Rare, slow, sometimes impossible.</summary>
    OfficiallyVerified = 2,

    /// <summary>Contradicted by something. Kept, excluded from calibration, never silently dropped.</summary>
    Disputed = 3
}

/// <summary>
/// What an area of a blueprint is missing, ranked so the gap list reads as advice rather than as a
/// list of failures.
/// </summary>
public enum CoverageGapKind
{
    /// <summary>No admitted evidence at all on this area. The worst case and the clearest message.</summary>
    NoEvidence = 0,

    /// <summary>Some evidence, below what the blueprint's weight asks for.</summary>
    ThinEvidence = 1,

    /// <summary>
    /// Plenty of evidence, none of it collected under conditions an exam claim can rest on. The
    /// learner has done the work; they have not yet done it the way the exam will ask.
    /// </summary>
    PracticeOnly = 2,

    /// <summary>The evidence is exam-grade and stale.</summary>
    StaleEvidence = 3
}
