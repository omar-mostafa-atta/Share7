using System.ComponentModel.DataAnnotations;
using Share7.Application.Economy.Models;
using Share7.Application.Rewards.Models;

namespace Share7.Application.Objectives.Models;

// ---- player-facing -----------------------------------------------------------------------------

/// <summary>
/// One objective as a player sees it: what it asks, how far they are, and whether there is
/// something to collect.
/// </summary>
public class ObjectiveDto
{
    /// <summary>The stable token. What the client maps art and any special-casing from.</summary>
    public string Key { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Already localised by the server. **There is no <c>nameEn</c>/<c>nameAr</c>** — the client
    /// never chooses between translations of backend content.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>A token the client resolves to its own sprite. Never a URL.</summary>
    public string? IconKey { get; init; }

    /// <summary>Progress so far, in the metric's unit. Clamped to <see cref="Target"/> for display.</summary>
    public long Value { get; init; }

    public long Target { get; init; }

    /// <summary>"InProgress" | "Completed" | "Claimed" | "Expired".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True when there is a reward waiting. The only flag a "collect" button needs.</summary>
    public bool CanClaim { get; init; }

    /// <summary>
    /// When this objective's window closes, or null for one that never resets. The client counts
    /// down against the server clock in the same payload, never the device's.
    /// </summary>
    public DateTime? CycleEndsAtUtc { get; init; }

    public int SortOrder { get; init; }
}

/// <summary>
/// What a claim paid.
/// <para>
/// Deliberately the same shape as the attempt and run responses: <c>rewards</c> are deltas to
/// animate, <c>balances</c> are absolute totals that already include them. Matching those means the
/// Unity client reuses its existing reconciler rather than growing a third way to apply currency.
/// </para>
/// </summary>
public class ObjectiveClaimResultDto
{
    public string Key { get; init; } = string.Empty;

    /// <summary>The objective's state after claiming — always <c>Claimed</c> on success.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>One entry per reward rule that fired. Empty when nothing was authored to pay.</summary>
    public IReadOnlyList<RewardDto> Rewards { get; init; } = [];

    /// <summary>Absolute balances afterwards. Assign these; never add the rewards.</summary>
    public IReadOnlyList<BalanceDto> Balances { get; init; } = [];
}

// ---- authoring (admin) -------------------------------------------------------------------------

public class ObjectiveTranslationRequest
{
    [Required]
    public Guid LangId { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }
}

public class CreateObjectiveRequest
{
    /// <summary>
    /// Lowercase, dot-separated, and **immutable once published** — the reward rule that pays for
    /// this keys on it.
    /// </summary>
    [Required]
    [MaxLength(128)]
    [RegularExpression("^[a-z][a-z0-9_.]*$",
        ErrorMessage = "Key must be lowercase letters, digits, underscores and dots, starting with a letter.")]
    public string Key { get; set; } = string.Empty;

    /// <summary><c>DAILY</c>, <c>WEEKLY</c>, <c>MONTHLY</c>, <c>SEASONAL</c> or <c>ACHIEVEMENT</c>.</summary>
    [Required]
    [MaxLength(32)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>A metric from <c>LeaderboardMetrics</c>. Anything else is refused at authoring.</summary>
    [Required]
    [MaxLength(48)]
    public string Metric { get; set; } = string.Empty;

    /// <summary>Optional sub-dimension — a pickup kind, a currency key. Null counts every scope.</summary>
    [MaxLength(64)]
    public string? Scope { get; set; }

    [Range(1, long.MaxValue)]
    public long Target { get; set; }

    /// <summary><c>SUM</c> (default), <c>BEST</c> or <c>LAST</c>.</summary>
    [MaxLength(16)]
    public string Aggregation { get; set; } = "SUM";

    public Guid? GameId { get; set; }
    public Guid? GradeId { get; set; }
    public Guid? LangId { get; set; }

    public DateTime? AvailableFromUtc { get; set; }
    public DateTime? AvailableToUtc { get; set; }

    /// <summary>A token the client maps to art. Never text, never a URL.</summary>
    [MaxLength(64)]
    public string? IconKey { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// At least one. Without a translation the objective has no name in any language, and a client
    /// would have nothing to render — refused rather than shipped blank.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "An objective needs at least one translation.")]
    public List<ObjectiveTranslationRequest> Translations { get; set; } = [];
}

/// <summary>
/// Updates an objective's presentation and availability.
/// <para>
/// <c>Key</c>, <c>Kind</c>, <c>Metric</c> and <c>Scope</c> are absent on purpose: changing any of
/// them would strand every progress row already counting under the old meaning, and the reward
/// transactions that paid against the old key. Retire it and author a new one.
/// </para>
/// </summary>
public class UpdateObjectiveRequest
{
    [Range(1, long.MaxValue)]
    public long Target { get; set; }

    public DateTime? AvailableFromUtc { get; set; }
    public DateTime? AvailableToUtc { get; set; }

    [MaxLength(64)]
    public string? IconKey { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    [Required]
    [MinLength(1, ErrorMessage = "An objective needs at least one translation.")]
    public List<ObjectiveTranslationRequest> Translations { get; set; } = [];
}

public class ObjectiveAdminDto
{
    public Guid ObjectiveId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Metric { get; init; } = string.Empty;
    public string? Scope { get; init; }
    public long Target { get; init; }
    public string Aggregation { get; init; } = string.Empty;
    public Guid? GameId { get; init; }
    public Guid? GradeId { get; init; }
    public Guid? LangId { get; init; }
    public DateTime? AvailableFromUtc { get; init; }
    public DateTime? AvailableToUtc { get; init; }
    public string? IconKey { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<ObjectiveTranslationRequest> Translations { get; init; } = [];
}
