using Share7.Domain.Content;
using Share7.Domain.Curriculum;
using Share7.Domain.Play;

namespace Share7.Domain.Evidence;

/// <summary>
/// One learner, one item, one answer, one moment, one set of conditions.
/// <para>
/// <b>Append-only. Never updated. Never deleted except for an erasure request.</b> This is the
/// foundation every educational capability rests on, and the only fully authoritative educational
/// fact in the system — everything downstream (observations, measurements, mastery, projections) is
/// derived from it and must be reconstructible by replaying it.
/// </para>
/// <para>
/// Before this table existed, <c>UserQuestionProgress</c> held <c>IsCorrect</c> and overwrote it on
/// every attempt: the chosen distractor arrived on the wire, was graded, was returned to the client
/// and was written nowhere; timing was never captured at all; and practice attempts recorded
/// nothing whatsoever. None of that was recoverable after the fact, which is why this shipped ahead
/// of everything else in the educational rebuild. See <c>Docs/EducationalArchitecture.md</c> §1.2.
/// </para>
/// </summary>
public class LearnerResponse
{
    public Guid Id { get; set; }

    /// <summary>
    /// Monotonic within the table. Projections track a watermark over this, exactly as
    /// <c>GameResult.Sequence</c> already does for leaderboards — the same pattern, applied to the
    /// domain that never had one.
    /// </summary>
    public long Sequence { get; set; }

    public Guid LearnerId { get; set; }

    /// <summary>
    /// The stable item this response is about — the identity that survives a rewrite and a
    /// language switch. **This is the join key for every measurement**: statistics, target
    /// mappings and attempt ordinals are all per item, not per rendering.
    /// </summary>
    public Guid ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>
    /// The immutable revision this response was given against: this key, these choices, this
    /// wording. Never edited, so replaying the log reconstructs exactly what the learner faced.
    /// </summary>
    public Guid ItemVersionId { get; set; }
    public ItemVersion? ItemVersion { get; set; }

    /// <summary>
    /// The per-language rendering the learner actually saw — today a <c>Question</c> row.
    /// <para>
    /// Nullable because not every future item kind has localized text, and because this is a
    /// presentation fact rather than an educational one: losing it would cost a diagnostic, losing
    /// <see cref="ItemVersionId"/> would cost the meaning of the answer.
    /// </para>
    /// </summary>
    public Guid? ItemLocalizationId { get; set; }
    public Question? ItemLocalization { get; set; }

    /// <summary>
    /// Which language the learner actually saw. Redundant with the localization today and kept
    /// separately anyway: it is the half of the question that must survive if the rendering is
    /// ever replaced.
    /// </summary>
    public Guid LangId { get; set; }

    // ---------------------------------------------------------------- what they did

    /// <summary>
    /// The choice the learner picked, or null when the item was never reached. **The distractor is
    /// the point**: which wrong answer a child chooses is the difference between a careless slip and
    /// a misconception, and it is what the previous schema discarded.
    /// </summary>
    public Guid? ChoiceId { get; set; }

    /// <summary>
    /// Server-graded, always. The client has no field in which to assert this, by the same rule that
    /// already governs scoring.
    /// </summary>
    public bool IsCorrect { get; set; }

    /// <summary>
    /// True when the payload named a choice that does not belong to this item — almost always a
    /// stale cached question set. Graded wrong rather than refused, and flagged here so the
    /// fingerprint stays visible to diagnostics instead of being indistinguishable from a wrong
    /// answer.
    /// </summary>
    public bool WasUnrecognised { get; set; }

    // ---------------------------------------------- conditions: what makes it interpretable

    /// <summary>
    /// How many times this learner has answered this item version before, counting from 1 for the
    /// first. **Derived server-side from this table**, never from anything the client says.
    /// </summary>
    public int AttemptOrdinal { get; set; }

    /// <summary>
    /// True when <see cref="AttemptOrdinal"/> is 1. Denormalised because every exam-like filter
    /// reads it and none of them want a computation in the predicate.
    /// </summary>
    public bool IsFirstEncounter { get; set; }

    public int HintsUsed { get; set; }

    /// <summary>How long the learner had the item in front of them. Client-measured — it is the only
    /// party that knows when the question appeared — and server-bounded before it is trusted.</summary>
    public int? ElapsedMs { get; set; }

    /// <summary>The limit the game imposed, or null when untimed.</summary>
    public int? TimeLimitMs { get; set; }

    /// <summary>
    /// Whether the learner could re-answer <i>this item within this administration</i>. Replaying
    /// the whole lesson later is a different administration and is captured by
    /// <see cref="AttemptOrdinal"/> instead.
    /// </summary>
    public bool RetryPermitted { get; set; }

    /// <summary>External help available — open book, teacher present, a co-op partner who knew.</summary>
    public bool WasAided { get; set; }

    public EvidenceDeliveryMode DeliveryMode { get; set; } = EvidenceDeliveryMode.Solo;

    /// <summary>
    /// Set when the answer was produced collaboratively, so the observation can be discounted rather
    /// than credited to one child. Null for solo play.
    /// </summary>
    public Guid? GroupId { get; set; }

    // ---------------------------------------------------- provenance: where it came from

    /// <summary>
    /// The published contract that admitted this response as evidence. **Non-nullable by design** —
    /// this column is the enforcement of the rule in <see cref="EvidenceContract"/>.
    /// </summary>
    public Guid EvidenceContractVersionId { get; set; }
    public EvidenceContractVersion? EvidenceContractVersion { get; set; }

    /// <summary>
    /// Which game asked. **Provenance, never partition** — the learner model is not keyed by game,
    /// because the game is how we found out rather than what is true. Nullable so a web quiz, a
    /// teacher's keyed-in paper test or a standalone assessment product needs no game at all.
    /// </summary>
    public Guid? GameId { get; set; }
    public Guid? ModeId { get; set; }
    public PlayContextKind PlayContext { get; set; } = PlayContextKind.Curriculum;
    public Guid? EventId { get; set; }

    // ----------------------------------------- educational context: what it was collected under

    /// <summary>
    /// The lesson node this response was collected under. Phase 1 renames the concept to a
    /// curriculum node; the GUIDs are preserved, so this column follows without a data migration.
    /// </summary>
    public Guid? NodeId { get; set; }

    /// <summary>The question-set version live when this was answered — what the learner was tested against.</summary>
    public int ContentVersion { get; set; }

    /// <summary>
    /// Denormalised for scope filtering once organizations exist. Null for every B2C learner, which
    /// is all of them today.
    /// </summary>
    public Guid? OrgId { get; set; }

    // ------------------------------------------------------------------------- time

    /// <summary>
    /// When the learner answered, by the client's clock. The ordering key, and **not** the
    /// trustworthy one: an offline device can be wrong or tampered with, which is why offline
    /// delivery caps evidence strength.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>When the server received it. The trustworthy timestamp.</summary>
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>
    /// The attempt's idempotency key, so a replayed submission is recognisable in the log rather
    /// than producing a second copy of the same evidence. Same pattern as
    /// <c>ProgressRequestLog.RequestId</c>.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
