using Share7.Domain.Games;
using Share7.Domain.Play;

namespace Share7.Domain.Evidence;

/// <summary>
/// The only bridge from gameplay to education, and the reason a game cannot assert that something
/// it measured means a child learned something.
/// <para>
/// A game produces <i>interactions</i>. A contract is the reviewed, published statement that a
/// particular interaction, in a particular game and mode, under stated conditions, constitutes
/// evidence about a learner. <b>Absent a contract, an interaction is telemetry, permanently.</b>
/// </para>
/// <para>
/// This is enforced structurally rather than by convention:
/// <see cref="LearnerResponse.EvidenceContractVersionId"/> is non-nullable, so there is no code
/// path that records evidence without naming a published contract. Distance travelled cannot
/// become mastery because no contract names it, and no contract can exist without a human
/// publishing one.
/// </para>
/// <para>See <c>Docs/EducationalArchitecture.md</c> §3.</para>
/// </summary>
public class EvidenceContract
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable, human-readable identity — <c>platform.lesson_attempt</c>,
    /// <c>game.runner.item_gate</c>. Referenced by seeds and by operators; never parsed for
    /// meaning.
    /// </summary>
    public string ContractKey { get; set; } = string.Empty;

    /// <summary>
    /// The game this contract binds, or null for a platform contract that applies to every game.
    /// <para>
    /// Resolution is **most-specific-wins**: a contract naming both a game and a mode beats one
    /// naming only the game, which beats the platform default. Two contracts at the same
    /// specificity for the same interaction kind is an authoring error and is refused at publish.
    /// </para>
    /// </summary>
    public Guid? GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>The mode this contract binds, or null for every mode of the game.</summary>
    public Guid? ModeId { get; set; }
    public GameMode? Mode { get; set; }

    /// <summary>Which kind of interaction this contract promotes. See <see cref="InteractionKinds"/>.</summary>
    public string InteractionKind { get; set; } = InteractionKinds.ItemResponse;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<EvidenceContractVersion> Versions { get; set; } = new List<EvidenceContractVersion>();
}

/// <summary>
/// One immutable revision of a contract. Every response names the version that admitted it, so
/// "why did this count, and how much did it count for" stays answerable after the rules change.
/// <para>
/// **Published versions are never edited.** A change inserts a new version and retires the
/// previous one, exactly as question rows already work — see
/// <c>Docs/EducationalArchitecture.md</c> §11.2.
/// </para>
/// </summary>
public class EvidenceContractVersion
{
    public Guid Id { get; set; }

    public Guid ContractId { get; set; }
    public EvidenceContract? Contract { get; set; }

    /// <summary>A human-readable label. The GUID is the identity; this is what an operator says out loud.</summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// The strength an observation gets when the conditions recorded on the response satisfy
    /// <see cref="RequiresFirstEncounter"/>, <see cref="RequiresUnhinted"/> and
    /// <see cref="RequiresNoRetry"/>.
    /// </summary>
    public EvidenceStrength StrengthWhenControlled { get; set; } = EvidenceStrength.Assessment;

    /// <summary>The strength when those conditions are not met. Never higher than the controlled value.</summary>
    public EvidenceStrength StrengthOtherwise { get; set; } = EvidenceStrength.Practice;

    /// <summary>
    /// Multiplier applied to an observation's weight. **Defaults to zero for
    /// <see cref="EvidenceStrength.Indicative"/> contracts** — indicative evidence accumulates
    /// before it counts, and the weight is raised only once it has been shown to predict.
    /// </summary>
    public decimal Weight { get; set; } = 1.0m;

    public bool RequiresFirstEncounter { get; set; } = true;
    public bool RequiresUnhinted { get; set; } = true;
    public bool RequiresNoRetry { get; set; } = true;

    /// <summary>
    /// Which play contexts this contract admits at all. A context outside this set produces no
    /// evidence from this contract. Stored as a bit flag over <see cref="PlayContextKind"/>.
    /// </summary>
    public int AdmittedContexts { get; set; } = AdmittedContextFlags.All;

    /// <summary>
    /// Whether an observation from this contract can be credited to one learner. False for
    /// collaborative interactions, which carry a group id instead.
    /// </summary>
    public bool IsIndividuallyAttributable { get; set; } = true;

    /// <summary>Why this contract was approved — the reviewer's justification, kept for audit.</summary>
    public string Justification { get; set; } = string.Empty;

    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public DateTime? RetiredAtUtc { get; set; }

    public bool IsPublished(DateTime atUtc) =>
        PublishedAtUtc is { } from && from <= atUtc && (RetiredAtUtc is not { } to || atUtc < to);

    public bool Admits(PlayContextKind context) =>
        (AdmittedContexts & AdmittedContextFlags.For(context)) != 0;

    /// <summary>
    /// The strength this contract assigns to a response, from the conditions recorded on it.
    /// <para>
    /// **Derived, never stored.** Strength is a function of (contract version, conditions), and
    /// both are immutable, so it recomputes identically forever. Storing it would put a derived
    /// value above the recompute line — see <c>Docs/EducationalArchitecture.md</c> §2.1.
    /// </para>
    /// </summary>
    public EvidenceStrength StrengthFor(
        bool isFirstEncounter, int hintsUsed, bool retryPermitted, EvidenceDeliveryMode delivery,
        bool wasAided = false)
    {
        var controlled =
            (!RequiresFirstEncounter || isFirstEncounter)
            && (!RequiresUnhinted || hintsUsed == 0)
            && (!RequiresNoRetry || !retryPermitted)

            // **Not a contract option, and deliberately.** §4.3 puts "not aided" in the exam-like
            // filter alongside first-encounter and unhinted, and there is no defensible contract
            // that says an answer produced with a teacher leaning over the desk generalises to a
            // sitting where nobody is. A contract may choose to ignore hints; it may not choose to
            // ignore this.
            && !wasAided;

        var strength = controlled ? StrengthWhenControlled : StrengthOtherwise;

        // Unverifiable conditions cannot support an exam-grade claim however controlled the client
        // says they were: an offline device's clock and retry behaviour are not observable.
        if (delivery == EvidenceDeliveryMode.Offline && strength > EvidenceStrength.Practice)
            strength = EvidenceStrength.Practice;

        // A group answer is not one child's answer. It is recorded, and it is not credited.
        if (!IsIndividuallyAttributable && strength > EvidenceStrength.Indicative)
            strength = EvidenceStrength.Indicative;

        return strength;
    }
}

/// <summary>Bit flags over <see cref="PlayContextKind"/>, so one column can admit a set of contexts.</summary>
public static class AdmittedContextFlags
{
    public const int Curriculum = 1 << 0;
    public const int FreePlay = 1 << 1;
    public const int Practice = 1 << 2;
    public const int Event = 1 << 3;
    public const int Assignment = 1 << 4;

    public const int All = Curriculum | FreePlay | Practice | Event | Assignment;

    public static int For(PlayContextKind context) => context switch
    {
        PlayContextKind.Curriculum => Curriculum,
        PlayContextKind.FreePlay => FreePlay,
        PlayContextKind.Practice => Practice,
        PlayContextKind.Event => Event,
        PlayContextKind.Assignment => Assignment,
        _ => 0
    };
}

/// <summary>Contract keys the platform seeds and the code may name.</summary>
public static class EvidenceContractKeys
{
    /// <summary>
    /// The platform's item-response contract: a lesson attempt posted to
    /// <c>POST /api/progress/attempts</c> is an item administration.
    /// <para>
    /// This is not a loophole in the "no contract, no evidence" rule — it is a published contract
    /// that says so, covering an endpoint which accepts nothing but graded item responses. Gameplay
    /// signals arrive on an entirely different path (<c>Run</c>, <c>TelemetryEvent</c>) which has no
    /// contract and no route into this schema at all.
    /// </para>
    /// </summary>
    public const string PlatformLessonAttempt = "platform.lesson_attempt";

    /// <summary>
    /// The platform's formal-sitting contract: an item served inside an
    /// <c>AssessmentAdministration</c> is an administration under the conditions that sitting
    /// declared.
    /// <para>
    /// Separate from the lesson contract because it admits a stronger claim on a stronger basis.
    /// The conditions are fixed server-side before the paper is served and cannot be edited
    /// afterwards, which is what an exam-grade claim actually rests on — and keeping the two
    /// contracts apart means a change to how lessons are treated cannot quietly re-grade every
    /// examination the platform has ever run.
    /// </para>
    /// </summary>
    public const string PlatformAssessmentAdministration = "platform.assessment_administration";
}
