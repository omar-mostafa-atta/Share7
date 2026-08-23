namespace Share7.Domain.Runs;

/// <summary>
/// One bounded activity that ends and settles — a runner track, a hub chest, a quiz bonus round.
/// **The unit of settlement, and the reason the client never decides what it earned.**
/// <para>
/// A run is opened by the server, which stamps the clock and issues the seed, and closed by a result
/// that reports *what was collected* and nothing about what it is worth. Everything between those two
/// points is presentation: the 3D coin still pops, the in-run counter still ticks, and neither has
/// touched a balance.
/// </para>
/// <para>
/// Nothing here is a currency amount. That is not an accident of the current shape — it is the
/// invariant the whole feature rests on, and there is a test that greps for its violation.
/// </para>
/// </summary>
public class Run
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Which mini-game. Scopes valuation lookup, so the same coin can be worth more in a harder game.</summary>
    public Guid GameId { get; set; }

    /// <summary>
    /// The procedural layout this run was generated from, issued here rather than chosen by the
    /// client. Unused in phase 1 beyond being handed back — it exists now because a run that was
    /// never issued a seed can never be verified retroactively, and re-deriving the layout from it is
    /// the difference between rejecting a forged claim exactly and rejecting it heuristically.
    /// </summary>
    public long Seed { get; set; }

    /// <summary>
    /// The multiplayer session this run belongs to, when it was networked. Recorded so a later phase
    /// can corroborate the claim against the session registry that already exists; not checked yet.
    /// </summary>
    public Guid? SessionId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// After this, the run stops being settleable. Bounds how long a started run can be held open and
    /// batched, and gives the sweep something to close against.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    public RunState State { get; set; } = RunState.Open;

    public RunOutcome Outcome { get; set; } = RunOutcome.Unknown;

    /// <summary>
    /// How long the client says the run lasted, **after clamping to the real elapsed server time**.
    /// The clamp is not defensive tidying: an unclamped duration is a free multiplier on any
    /// per-second plausibility bound, which is the whole point of having one.
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>The caller's idempotency key for <c>/runs/start</c>. A retry returns the same run and seed.</summary>
    public string? StartRequestId { get; set; }

    /// <summary>
    /// The caller's idempotency key for <c>/runs/{id}/result</c>. Separate from
    /// <see cref="StartRequestId"/> because they are two requests with two keys — one column for both
    /// would have the start burn the key the result needs.
    /// </summary>
    public string? ResultRequestId { get; set; }

    /// <summary>
    /// Something about this run did not add up and it was capped rather than refused. **Flagged, paid
    /// and recorded — never thrown away.** A child on a device with a bad clock, or one whose session
    /// dropped and resumed, must not lose a legitimate run and have no way to explain why.
    /// </summary>
    public bool IsFlagged { get; set; }

    /// <summary>Machine token for why, e.g. <c>duration_clamped</c>, <c>pickup_capped</c>. Comma-joined when several apply.</summary>
    public string? FlagReason { get; set; }

    /// <summary>
    /// Whether the payout was shortened by a cap. Surfaced to the client because a run that shows 47
    /// collected and pays 20 has to be able to say so — silently paying less is how a child learns
    /// the game is unfair.
    /// </summary>
    public bool CapReached { get; set; }

    /// <summary>Machine token the client localises, e.g. <c>pickup_limit</c>. Null when nothing capped.</summary>
    public string? CapMessage { get; set; }

    /// <summary>
    /// What the client reported, verbatim, as JSON. Kept raw so a settlement can be re-explained
    /// months later against a valuation table that has since changed, and so seed verification has
    /// the original claim to check rather than a summary of it.
    /// </summary>
    public string PickupsJson { get; set; } = "[]";

    /// <summary>Declared modifiers, verbatim. Same reasoning as <see cref="PickupsJson"/>.</summary>
    public string? ModifiersJson { get; set; }


    /// <summary>
    /// Which layout generator version this run was issued under, or <c>0</c> when the game has none.
    /// <para>
    /// **Stamped at start and never re-read.** A client mid-rollout generated its track with the
    /// generator that was live when it began; verifying it against whichever version the server
    /// prefers by the time the result arrives would reject correct runs for the crime of being queued
    /// during a deploy.
    /// </para>
    /// </summary>
    public int LayoutVersion { get; set; }

    /// <summary>When an admin looked at this flagged run. Null while it is still waiting.</summary>
    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>Who reviewed it. Not a foreign key — the review has to stay legible after they leave.</summary>
    public Guid? ReviewedByUserId { get; set; }

    /// <summary>What they concluded. Free text, for humans.</summary>
    public string? ReviewNote { get; set; }

    public ICollection<RunPayout> Payouts { get; set; } = new List<RunPayout>();
}
