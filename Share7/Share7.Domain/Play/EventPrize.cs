using Share7.Domain.Leaderboards;
using Share7.Domain.Rewards;

namespace Share7.Domain.Play;

/// <summary>What an event's prize actually consists of.</summary>
public enum EventPrizeKind
{
    /// <summary>
    /// Currency, products, or both — everything the platform can hand over itself. Paid by the same
    /// reward engine that pays a lesson, so it lands in the same ledger with the same idempotency.
    /// </summary>
    InGame = 0,

    /// <summary>
    /// Something outside the game: money, a device, a voucher. **The platform never delivers one
    /// itself.** It records that the placing won it and opens a claim a person fulfils, because a
    /// child's address and a payment are not things this system holds.
    /// </summary>
    RealWorld = 1
}

/// <summary>
/// One band of an event's prize table: "ranks 1 to 3 get this".
/// <para>
/// <b>Bands here are authored ranges, not the fixed ladder the weekly boards use.</b> A weekly board
/// pays through <c>LeaderboardRankBands</c>, where every band a rank falls inside pays and the set is
/// hard-coded. An event's table is written per event by the operator running it, so the ranges are
/// data — and each placing matches <b>exactly one</b> tier, the narrowest one containing its rank,
/// because "first place also collects the top-ten prize" is not what anybody means by a prize table.
/// </para>
/// </summary>
public class EventPrizeTier
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }
    public PlayEvent? Event { get; set; }

    /// <summary>Best rank this tier covers, inclusive. 1 is first place.</summary>
    public int FromRank { get; set; } = 1;

    /// <summary>Worst rank this tier covers, inclusive.</summary>
    public int ToRank { get; set; } = 1;

    public EventPrizeKind Kind { get; set; }

    /// <summary>
    /// For an in-game tier: the rule carrying what it pays — currencies, products, or both.
    /// <para>
    /// A reward rule rather than a second grants table of its own. That is not reuse for its own
    /// sake: the rule path already owns idempotent payment, savepoint rollback when a grant cannot
    /// be made, retired-currency handling and the ledger rows that explain a payout months later.
    /// A parallel implementation would have to earn all of that again, and would be the second
    /// answer to "how did this child get these coins".
    /// </para>
    /// </summary>
    public Guid? RewardRuleId { get; set; }
    public RewardRule? RewardRule { get; set; }

    /// <summary>
    /// For a real-world tier: what it is worth, in the smallest unit of <see cref="ValueCurrencyCode"/>.
    /// Recorded for the operator's own reporting and audit, never shown as a price.
    /// </summary>
    public long? DeclaredValueMinor { get; set; }

    /// <summary>ISO 4217 code for <see cref="DeclaredValueMinor"/>, e.g. <c>EGP</c>.</summary>
    public string? ValueCurrencyCode { get; set; }

    /// <summary>
    /// How many of this prize exist. Enforced when awards are written, so a tier spanning ranks 1–10
    /// with three physical prizes hands out three and marks the rest as not awarded rather than
    /// promising ten.
    /// </summary>
    public int? Quantity { get; set; }

    public int SortOrder { get; set; }

    public ICollection<EventPrizeTierTranslation> Translations { get; set; } =
        new List<EventPrizeTierTranslation>();

    /// <summary>Whether <paramref name="rank"/> falls in this band.</summary>
    public bool Covers(int rank) => rank >= FromRank && rank <= ToRank;

    /// <summary>How many ranks the band spans. Used to order overlapping tiers narrowest-first.</summary>
    public int Width => Math.Max(0, ToRank - FromRank) + 1;
}

/// <summary>
/// A prize's title and description in one language — "1,000 coins", "A tablet", "500 EGP".
/// <para>
/// Authored text even for in-game tiers, rather than rendered from the grants: an operator writing
/// "a legendary skin and 1,000 coins" is making a promise, and a client assembling that sentence out
/// of grant rows would eventually word it differently from the event that advertised it.
/// </para>
/// </summary>
public class EventPrizeTierTranslation
{
    public Guid TierId { get; set; }
    public EventPrizeTier? Tier { get; set; }

    public Guid LangId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

/// <summary>Where an award is in its life. In-game awards are born <see cref="Granted"/>; real-world ones are not.</summary>
public enum EventAwardState
{
    /// <summary>The prize has been handed over in full. Terminal for an in-game tier.</summary>
    Granted = 0,

    /// <summary>A real-world prize that has been won and is waiting on the claim workflow.</summary>
    AwaitingClaim = 1,

    /// <summary>The claim finished and a person recorded the prize as delivered.</summary>
    Fulfilled = 2,

    /// <summary>Nobody claimed it in time, or the winner declined. Terminal, and auditable.</summary>
    Forfeited = 3,

    /// <summary>The event was cancelled, or the placing was disqualified after settlement.</summary>
    Void = 4
}

/// <summary>
/// One placing's prize, written when the event's cycle settles. **Append-once, one per (event,
/// cohort, user).**
/// <para>
/// Separate from <c>LeaderboardSettlement</c>, which records where everybody finished. This records
/// what that finish won, which is a different fact with a different lifetime: a rebuild may
/// recalculate an entry, and must never be able to change a prize somebody was already told about.
/// </para>
/// </summary>
public class EventAward
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }
    public PlayEvent? Event { get; set; }

    public Guid TierId { get; set; }
    public EventPrizeTier? Tier { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Which cohort's ladder this placing was on, and the key of that cohort (the grade, or empty for All).</summary>
    public LeaderboardCohort Cohort { get; set; }

    public Guid CohortKey { get; set; }

    public int FinalRank { get; set; }

    /// <summary>The ranked value the placing was won with, copied so a prize can be explained later.</summary>
    public long Value { get; set; }

    public EventAwardState State { get; set; }

    /// <summary>
    /// The reward transaction that paid an in-game tier, when one did. Null for a real-world prize,
    /// and null for a tier whose rule paid nothing.
    /// </summary>
    public Guid? RewardTransactionId { get; set; }

    /// <summary>
    /// When the winner was shown this award in the app. Null until then, and what the client's
    /// "you won" surface keys on — an award nobody has been told about is not the same as one they
    /// have seen and dismissed.
    /// </summary>
    public DateTime? SeenAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public PrizeClaim? Claim { get; set; }
}

/// <summary>Where a real-world prize's fulfilment has got to. Moved only by a person.</summary>
public enum PrizeClaimState
{
    /// <summary>Waiting for an operator to look at it. Every claim starts here.</summary>
    PendingReview = 0,

    /// <summary>
    /// Approved, and waiting on a guardian to be contacted and arrangements made **outside this
    /// system**. No address, phone number or payment detail is ever stored here.
    /// </summary>
    AwaitingGuardian = 1,

    /// <summary>A person recorded the prize as delivered.</summary>
    Fulfilled = 2,

    /// <summary>Nobody responded in time. The award is forfeited with it.</summary>
    Forfeited = 3,

    /// <summary>Refused after review — a disqualified run, a duplicate account, a broken rule.</summary>
    Rejected = 4
}

/// <summary>
/// The fulfilment record for a real-world prize.
/// <para>
/// <b>It deliberately holds no personal data.</b> No address, no phone number, no bank detail, no
/// guardian name. The users are children; the contact that fulfilment needs happens through whatever
/// channel the operator already has a lawful basis for, and this row records only that it is
/// happening and how it ended. A prize workflow is not a reason to start collecting a child's address.
/// </para>
/// </summary>
public class PrizeClaim
{
    public Guid Id { get; set; }

    public Guid AwardId { get; set; }
    public EventAward? Award { get; set; }

    public Guid UserId { get; set; }

    public PrizeClaimState State { get; set; }

    /// <summary>
    /// When the claim lapses if nothing has moved. Set from the event's authored claim window, so an
    /// unclaimed prize becomes a fact rather than an open obligation with no end.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Free text for humans, the audit trail of who did what. Never shown to the winner.</summary>
    public string? ReviewNote { get; set; }

    /// <summary>Who last moved it. Not a foreign key — the record has to stay legible after they leave.</summary>
    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public DateTime? FulfilledAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// The transitions a person may make, from each state. Encoded here rather than in the admin
    /// service so the rule is one list: a claim moves forward, and the three endings are terminal.
    /// </summary>
    public static bool CanTransition(PrizeClaimState from, PrizeClaimState to) => from switch
    {
        PrizeClaimState.PendingReview =>
            to is PrizeClaimState.AwaitingGuardian or PrizeClaimState.Rejected or PrizeClaimState.Forfeited,
        PrizeClaimState.AwaitingGuardian =>
            to is PrizeClaimState.Fulfilled or PrizeClaimState.Forfeited or PrizeClaimState.Rejected,
        _ => false
    };
}
