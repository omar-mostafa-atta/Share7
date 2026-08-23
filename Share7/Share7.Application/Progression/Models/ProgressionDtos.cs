using System.ComponentModel.DataAnnotations;
using Share7.Application.Objectives.Models;

namespace Share7.Application.Progression.Models;

// ---- player-facing ---------------------------------------------------------------------------

/// <summary>
/// Where a player stands on the level curve. **Computed server-side on every read** — the client
/// never holds the curve and never derives a level.
/// <para>
/// Two implementations of one curve is a bug generator, and the failure is invisible: a build that
/// shipped before the curve was tuned would show a level the server disagrees with, on a number the
/// child treats as a fact about themselves. One round trip is cheaper than that.
/// </para>
/// </summary>
public class PlayerLevelDto
{
    public int Level { get; init; }

    /// <summary>
    /// Total XP. Also lifetime XP earned — the two are the same number because XP is
    /// non-spendable, which is the whole reason the level is cheap to compute.
    /// </summary>
    public long Xp { get; init; }

    /// <summary>XP earned since this level began. <c>0</c> the instant a level is reached.</summary>
    public long XpIntoLevel { get; init; }

    /// <summary>
    /// The size of the current level's band — how much XP separates this level from the next.
    /// <c>0</c> at max level.
    /// <para>
    /// A band width rather than an absolute threshold because that is what a progress bar needs:
    /// <c>XpIntoLevel / XpForNextLevel</c> fills it with no second subtraction on the client.
    /// </para>
    /// </summary>
    public long XpForNextLevel { get; init; }

    /// <summary>Remaining XP to the next level. <c>0</c> at max level.</summary>
    public long XpToNextLevel { get; init; }

    /// <summary>
    /// True when the curve has no rung above this one. The client must render this rather than
    /// showing a bar that can never fill.
    /// </summary>
    public bool IsMaxLevel { get; init; }
}

/// <summary>
/// The progression snapshot, <c>GET /api/progression/me</c>.
/// <para>
/// Carries only the level today. Quests, achievements and streaks join this object rather than
/// getting endpoints of their own — one call populates the home screen, and adding a field is not a
/// breaking change where adding a round trip is.
/// </para>
/// </summary>
public class ProgressionSnapshotDto
{
    public PlayerLevelDto Level { get; init; } = new();

    /// <summary>Quests resetting today. Empty until some are authored.</summary>
    public IReadOnlyList<ObjectiveDto> Daily { get; init; } = [];

    /// <summary>Quests resetting this week.</summary>
    public IReadOnlyList<ObjectiveDto> Weekly { get; init; } = [];

    /// <summary>Objectives that never reset, and their badges once those exist.</summary>
    public IReadOnlyList<ObjectiveDto> Achievements { get; init; } = [];

    /// <summary>The player's daily streak. Zeroes for someone who has never played.</summary>
    public StreakDto Streak { get; init; } = new();

    /// <summary>The server's clock, so the client never compares against the device's.</summary>
    public DateTime ServerTimeUtc { get; init; }
}

/// <summary>
/// A consecutive-day streak.
/// <para>
/// <see cref="FreezesRemaining"/> is surfaced deliberately: a child who missed a day and kept their
/// streak should be told a freeze covered it, not left to think the rule is inconsistent.
/// </para>
/// </summary>
public class StreakDto
{
    public int Current { get; init; }
    public int Best { get; init; }
    public int FreezesRemaining { get; init; }
}

// ---- curve authoring (admin) -----------------------------------------------------------------

public class LevelThresholdDto
{
    public int Level { get; init; }
    public long CumulativeXp { get; init; }
}

/// <summary>
/// Replaces the **entire** curve in one call.
/// <para>
/// Whole-curve replacement rather than per-rung edits, because the invariants are properties of the
/// set — level 1 starts at zero, levels are contiguous from 1, thresholds strictly increase — and
/// none of them can be checked while looking at a single row. A per-rung endpoint would let an
/// operator leave the curve briefly invalid, which is exactly when someone levels up.
/// </para>
/// </summary>
public class ReplaceLevelCurveRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "A level curve needs at least one level.")]
    [MaxLength(500, ErrorMessage = "A level curve is capped at 500 levels.")]
    public List<LevelThresholdEntryRequest> Levels { get; set; } = [];
}

public class LevelThresholdEntryRequest
{
    [Range(1, 500)]
    public int Level { get; set; }

    [Range(0, long.MaxValue)]
    public long CumulativeXp { get; set; }
}
