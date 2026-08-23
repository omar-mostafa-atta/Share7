using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Leaderboards.Models;

/// <summary>
/// Authors a board. **A board is data, so this is the whole of "adding a leaderboard"** — no
/// migration, no deploy, no client release.
/// </summary>
public class SaveLeaderboardBoardRequest
{
    /// <summary>
    /// Stable public name, <c>{scope}.{subject}.{metric}.{period}</c>, lowercase and dot-separated.
    /// <para>
    /// Capped at 110 rather than 128 so <c>{boardKey}:{rankBand}</c> still fits a reward rule's
    /// reference key at settlement. **Never renamed once published** — analytics and prizes key on
    /// it, so a rename is a new board with no history.
    /// </para>
    /// </summary>
    [Required]
    [MaxLength(110)]
    [RegularExpression("^[a-z0-9_.-]+$",
        ErrorMessage = "A board key is lowercase letters, digits, dots, dashes and underscores.")]
    public string BoardKey { get; set; } = string.Empty;

    /// <summary>A value from <c>LeaderboardMetrics</c>. Anything else is refused at authoring.</summary>
    [Required]
    [MaxLength(48)]
    public string Metric { get; set; } = string.Empty;

    /// <summary><c>Desc</c> (default) or <c>Asc</c> for metrics where lower is better.</summary>
    [MaxLength(8)]
    public string SortDirection { get; set; } = "Desc";

    /// <summary><c>Best</c> (default), <c>Sum</c> or <c>Last</c>.</summary>
    [MaxLength(8)]
    public string Aggregation { get; set; } = "Best";

    /// <summary><c>AllTime</c>, <c>Daily</c>, <c>Weekly</c>, <c>Monthly</c> or <c>Event</c>.</summary>
    [MaxLength(16)]
    public string Period { get; set; } = "AllTime";

    /// <summary>Comma-separated cohort names. Only <c>All</c> and <c>Grade</c> are resolvable today.</summary>
    [MaxLength(128)]
    public string SupportedCohorts { get; set; } = "All";

    /// <summary>Restrict to one game's results, or null to span the platform.</summary>
    public Guid? GameId { get; set; }

    /// <summary>Restrict to one grade, so a KG1 child and a Grade 6 child are not on one ladder.</summary>
    public Guid? GradeId { get; set; }

    public Guid? LangId { get; set; }

    /// <summary>
    /// How deep an unentitled caller may read. Null for no limit.
    /// <para>
    /// Visibility only. It must never change anyone's value or rank — that is the no-pay-to-win
    /// commitment, and also why the top page stays publicly cacheable.
    /// </para>
    /// </summary>
    public int? VisibleRankLimit { get; set; }

    /// <summary>Seconds after a cycle ends that a late result still counts. A poor connection is not cheating.</summary>
    [Range(0, 3600)]
    public int GraceSeconds { get; set; } = 60;

    public bool IsActive { get; set; } = true;

    /// <summary>Title and description per language. At least one is required.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "A board needs a name in at least one language.")]
    public List<LeaderboardBoardTranslationRequest> Translations { get; set; } = [];
}

public class LeaderboardBoardTranslationRequest
{
    [Required]
    public Guid LangId { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }
}

/// <summary>Authors an event cycle by hand, for boards whose window is not derived from a calendar.</summary>
public class CreateLeaderboardCycleRequest
{
    [Required]
    public DateTime StartsAtUtc { get; set; }

    [Required]
    public DateTime EndsAtUtc { get; set; }
}

/// <summary>A board as an operator sees it, including the parts players never do.</summary>
public class LeaderboardBoardAdminDto
{
    public Guid BoardId { get; set; }
    public string BoardKey { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public string SortDirection { get; set; } = string.Empty;
    public string Aggregation { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string SupportedCohorts { get; set; } = string.Empty;
    public Guid? GameId { get; set; }
    public Guid? GradeId { get; set; }
    public Guid? LangId { get; set; }
    public int? VisibleRankLimit { get; set; }
    public int GraceSeconds { get; set; }
    public bool IsActive { get; set; }
    public int CycleCount { get; set; }
    public IReadOnlyList<LeaderboardBoardTranslationRequest> Translations { get; set; } = [];
}

/// <summary>Authors what a believable result looks like for one game and metric.</summary>
public class SaveMetricBoundRequest
{
    /// <summary>Null applies to every game raising this metric.</summary>
    public Guid? GameId { get; set; }

    [Required]
    [MaxLength(48)]
    public string Metric { get; set; } = string.Empty;

    /// <summary>Largest single believable value. Null for no ceiling.</summary>
    public long? MaxValue { get; set; }

    /// <summary>Most results per player per UTC day. Catches what a value ceiling cannot.</summary>
    public int? MaxResultsPerDay { get; set; }

    /// <summary>Most total value per player per UTC day. The bound that matters for Sum boards.</summary>
    public long? MaxValuePerDay { get; set; }

    public bool Enabled { get; set; } = true;
}

public class MetricBoundDto
{
    public Guid Id { get; set; }
    public Guid? GameId { get; set; }
    public string Metric { get; set; } = string.Empty;
    public long? MaxValue { get; set; }
    public int? MaxResultsPerDay { get; set; }
    public long? MaxValuePerDay { get; set; }
    public bool Enabled { get; set; }
}

/// <summary>A result held out of ranking, waiting for somebody to decide.</summary>
public class FlaggedResultDto
{
    public Guid ResultId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The player's public handle, so a reviewer never needs to see their real name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public Guid GameId { get; set; }
    public string Metric { get; set; } = string.Empty;
    public long Value { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? FlagReason { get; set; }
}

/// <summary>What a reviewer decided about a flagged result.</summary>
public class ResolveFlagRequest
{
    /// <summary>
    /// True to clear the flag and let the result rank. False to leave it excluded permanently.
    /// <para>
    /// Either way the row survives. A reviewer's decision is a judgement, and judgements get
    /// revisited — deleting the evidence would make that impossible.
    /// </para>
    /// </summary>
    public bool Legitimate { get; set; }
}
