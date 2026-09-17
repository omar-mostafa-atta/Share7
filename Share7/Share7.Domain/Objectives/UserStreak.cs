namespace Share7.Domain.Objectives;

/// <summary>
/// How many cycles in a row a player has shown up.
/// <para>
/// **The one thing in this subsystem an objective counter genuinely cannot express.** Every
/// objective accumulates; a streak needs <i>consecutiveness</i>, and there is no aggregation over a
/// counter that distinguishes "seven days running" from "seven days, spread over a month". Hence a
/// table of its own — the only new state Phase 5 adds.
/// </para>
/// </summary>
public class UserStreak
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Which streak this is — <c>daily</c> today. A key rather than a single implicit streak so a
    /// weekly or per-game one can exist later without a schema change.
    /// </summary>
    public string StreakKey { get; set; } = string.Empty;

    /// <summary>Consecutive cycles including the current one.</summary>
    public int Current { get; set; }

    /// <summary>
    /// The best run ever. **Never decreases** — a broken streak resets <see cref="Current"/> and
    /// leaves this alone, so what a child achieved stays theirs.
    /// </summary>
    public int Best { get; set; }

    /// <summary>
    /// The last cycle that counted. Compared against the current one to decide whether the streak
    /// continues, holds, or breaks — which is why the cycle key had to be a stored, derivable
    /// string rather than a timestamp.
    /// </summary>
    public string LastCycleKey { get; set; } = string.Empty;

    /// <summary>
    /// Missed cycles this player may skip without breaking their streak.
    /// <para>
    /// **A duty of care, not a monetisation hook.** Streak-loss anxiety in children is well
    /// documented and this platform's audience starts at age three; a streak that punishes a child
    /// for one bad evening teaches the wrong thing about learning. Forgiveness is built in from the
    /// first version rather than softened in later, and freezes regenerate on their own — they are
    /// deliberately not purchasable.
    /// </para>
    /// </summary>
    public int FreezesRemaining { get; set; }

    /// <summary>When a freeze was last handed back, so regeneration is rate-limited.</summary>
    public DateTime? FreezeRegeneratedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>Streak keys the code names. Constants, so a typo is a build error.</summary>
public static class StreakKeys
{
    public const string Daily = "daily";
}
