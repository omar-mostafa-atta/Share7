using System.ComponentModel.DataAnnotations;
using Share7.Application.Economy.Models;
using Share7.Domain.Runs;

namespace Share7.Application.Runs.Models;

// ---- starting a run --------------------------------------------------------------------------

/// <summary>
/// Opens a run. Carries no currency, no amount and no claim about the layout — the server issues the
/// seed and stamps the clock, which is what makes everything the result later reports checkable.
/// </summary>
public class StartRunRequest
{
    [Required]
    public Guid GameId { get; set; }

    /// <summary>
    /// The multiplayer session this run belongs to, when it is networked. Recorded, not yet checked:
    /// corroborating it against the session registry is phase 3.
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Optional idempotency key. Generate one per run and **reuse it for every retry of that start** —
    /// a retry returns the same <c>runId</c> and the same <c>seed</c> rather than opening a second run,
    /// which matters because the client generates its track from that seed and two seeds is two tracks.
    /// </summary>
    [MaxLength(128)]
    public string? RequestId { get; set; }
}

public class StartRunResponse
{
    public Guid RunId { get; init; }
    public Guid GameId { get; init; }

    /// <summary>
    /// The layout this run is generated from. Server-issued from a cryptographic RNG, never chosen by
    /// the client — a client-chosen seed can be re-picked until it yields a rich track, and cannot be
    /// used to check anything afterwards.
    /// </summary>
    public long Seed { get; init; }

    public DateTime StartedAtUtc { get; init; }
    public DateTime ExpiresAtUtc { get; init; }

    /// <summary>
    /// The server's clock, so a device with a wrong one can still show a correct countdown to
    /// <see cref="ExpiresAtUtc"/> rather than an expiry that has already passed or never arrives.
    /// </summary>
    public DateTime ServerTimeUtc { get; init; }
}

// ---- settling a run --------------------------------------------------------------------------

/// <summary>How many of one kind of pickup were collected. A count of things, <b>not</b> a balance.</summary>
public class RunPickupReport
{
    /// <summary>
    /// A <see cref="PickupKinds"/> token, e.g. <c>coin</c>. An unknown kind pays zero rather than
    /// failing the run.
    /// </summary>
    [Required]
    [MaxLength(PickupKinds.MaxLength)]
    public string Kind { get; set; } = string.Empty;

    [Range(0, 1_000_000)]
    public int Count { get; set; }
}

/// <summary>
/// A modifier the run declares was active, and for how long. **It declares that the modifier ran; it
/// does not declare what it is worth** — the server owns the multiplier, because a client that
/// multiplies its own payout controls its own payout.
/// </summary>
public class RunModifierReport
{
    /// <summary><c>double_reward</c>. An unrecognised kind is ignored, which can only ever pay less.</summary>
    [Required]
    [MaxLength(48)]
    public string Kind { get; set; } = string.Empty;

    [Range(0, 86_400)]
    public double DurationSeconds { get; set; }
}

/// <summary>
/// What a finished run reports. **There is no field here in which a client can assert a currency, an
/// amount or a balance** — that absence is the feature, and a test enforces it by reflection so it
/// cannot be eroded by a later convenience.
/// </summary>
public class SubmitRunResultRequest
{
    /// <summary>One entry per kind. A kind repeated across entries is summed, then capped as one total.</summary>
    public List<RunPickupReport> Pickups { get; set; } = [];

    public List<RunModifierReport> Modifiers { get; set; } = [];

    /// <summary>
    /// How long the client says the run lasted. **Clamped to real elapsed server time**, never
    /// trusted — an unclamped duration is a free multiplier on every per-second bound.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int DurationMs { get; set; }

    /// <summary><c>Completed</c>, <c>Failed</c> or <c>Abandoned</c>. Anything else settles as <c>Unknown</c>.</summary>
    [MaxLength(32)]
    public string Outcome { get; set; } = nameof(RunOutcome.Completed);

    /// <summary>
    /// Which individual pickups were taken. Optional and unused in phase 1 — stored verbatim so that
    /// when layout re-derivation lands there is a claim on file to check, rather than only a total.
    /// </summary>
    public List<int>? PickupIds { get; set; }

    /// <summary>
    /// Optional idempotency key for **this result**. Reuse it across retries: the offline queue
    /// retries on reconnect by design, so a replay is the normal path rather than an edge case.
    /// </summary>
    [MaxLength(128)]
    public string? RequestId { get; set; }
}

// ---- the settlement --------------------------------------------------------------------------

/// <summary>What the run reported, echoed back. Counts of things collected — not currency.</summary>
public class RunCollectedDto
{
    public string Kind { get; init; } = string.Empty;
    public int Count { get; init; }
}

/// <summary>
/// One currency credited by the settlement, and what produced it. A **delta** — what to animate —
/// unlike <see cref="BalanceDto"/>, which is absolute.
/// <para>
/// Keyed on the currency <c>key</c> rather than its row id, matching <c>RewardGrantDto</c> and
/// <c>BalanceDto</c>: the key is what the client caches balances against, and it survives the row
/// being re-seeded in a fresh environment.
/// </para>
/// </summary>
public class RunRewardDto
{
    public string Currency { get; init; } = string.Empty;
    public long Amount { get; init; }

    /// <summary>
    /// <c>pickup:{kind}</c> or <c>rule:{ruleId}</c>. Two mechanisms pay a run — a variable payout
    /// scaling with what was collected, and a fixed reward rule — and the results screen reads
    /// better when it can tell "47 coins collected" apart from "run completed bonus".
    /// </summary>
    public string Source { get; init; } = string.Empty;
}

/// <summary>
/// The server's answer to a finished run: what it counted, what it paid, and what the balances are
/// now.
/// <para>
/// <c>rewards</c> are deltas; <c>balances</c> are **absolute totals that already include them**.
/// Assign the balances over the local wallet — do not add the rewards to it — exactly as
/// <c>AttemptResultDto</c> works, so the same reconciler serves both.
/// </para>
/// </summary>
public class RunSettlementDto
{
    public Guid RunId { get; init; }
    public Guid GameId { get; init; }

    /// <summary><c>Settled</c> for anything that paid.</summary>
    public string State { get; init; } = string.Empty;

    public string Outcome { get; init; } = string.Empty;

    public IReadOnlyList<RunCollectedDto> Collected { get; init; } = [];

    public IReadOnlyList<RunRewardDto> Rewards { get; init; } = [];

    public IReadOnlyList<BalanceDto> Balances { get; init; } = [];

    /// <summary>
    /// True when a cap shortened the payout. **Not optional, and not cosmetic** — a results screen
    /// that shows 47 collected and then pays 20 has to be able to say why. Silently paying less is
    /// how a child learns the game is unfair.
    /// </summary>
    public bool CapReached { get; init; }

    /// <summary>
    /// Machine token the client localises: <c>pickup_limit</c> today, <c>daily_coin_limit</c> once
    /// the account ceiling lands. Null when nothing capped.
    /// </summary>
    public string? CapMessage { get; init; }

    public DateTime ServerTimeUtc { get; init; }
}
