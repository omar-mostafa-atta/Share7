namespace Share7.Application.Leaderboards.Models;

/// <summary>One board as offered to a caller, with whichever cycle is currently live.</summary>
public class LeaderboardBoardDto
{
    public Guid BoardId { get; set; }

    /// <summary>Stable public name. Analytics and reward rules key on this, never on the id.</summary>
    public string BoardKey { get; set; } = string.Empty;

    /// <summary>Already localised. There is no <c>nameEn</c>/<c>nameAr</c> to choose between.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Metric { get; set; } = string.Empty;

    public string SortDirection { get; set; } = string.Empty;

    public string Period { get; set; } = string.Empty;

    public Guid? GameId { get; set; }

    /// <summary>Cohorts this caller may actually ask for, already filtered by what they belong to.</summary>
    public IReadOnlyList<string> SupportedCohorts { get; set; } = [];

    public LeaderboardCycleDto? CurrentCycle { get; set; }
}

public class LeaderboardCycleDto
{
    public Guid CycleId { get; set; }

    public DateTime StartsAtUtc { get; set; }

    /// <summary>Null for a cycle that never ends, rather than a sentinel the client has to know about.</summary>
    public DateTime? EndsAtUtc { get; set; }

    public string State { get; set; } = string.Empty;

    public int TotalRanked { get; set; }
}

/// <summary>One row on a board.</summary>
public class LeaderboardEntryDto
{
    public int Rank { get; set; }

    /// <summary>
    /// Present so the client can mark the caller's own row and, later, open a profile. It is the
    /// **only** identifier on this object — no email, no username, no grade, no real name.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>A generated handle. Never anything the account was registered with.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarKey { get; set; }

    public long Value { get; set; }

    public DateTime AchievedAtUtc { get; set; }

    public bool IsSelf { get; set; }
}

/// <summary>A page of a board.</summary>
public class LeaderboardPageDto
{
    public Guid CycleId { get; set; }

    public string Cohort { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public IReadOnlyList<LeaderboardEntryDto> Entries { get; set; } = [];

    /// <summary>Opaque and signed. Null when there is no further page.</summary>
    public string? NextCursor { get; set; }

    public int TotalRanked { get; set; }

    /// <summary>
    /// Set when the caller's entitlement stopped the listing short, so the client can offer an
    /// upgrade rather than rendering a misleading end-of-list.
    /// </summary>
    public int? TruncatedAtRank { get; set; }

    /// <summary>
    /// On every response, so a countdown never costs a second round trip and never has to trust
    /// the device clock.
    /// </summary>
    public DateTime ServerTimeUtc { get; set; }
}

/// <summary>The caller's own standing. The cheapest read, and the one the HUD uses.</summary>
public class LeaderboardStandingDto
{
    public Guid CycleId { get; set; }

    public string Cohort { get; set; } = string.Empty;

    /// <summary>
    /// Null when the player holds no entry yet. That is an ordinary state — "you have not played
    /// this week" — and deliberately not a 404.
    /// </summary>
    public int? Rank { get; set; }

    public long? Value { get; set; }

    public int TotalRanked { get; set; }

    /// <summary>
    /// How far up the board they are, 0–100. Null when unranked. Rounded, because a child does not
    /// need three decimal places to know they are doing well.
    /// </summary>
    public int? Percentile { get; set; }

    /// <summary>Whether they are excluded from public listings. They still have a rank.</summary>
    public bool IsHidden { get; set; }

    public DateTime ServerTimeUtc { get; set; }
}

/// <summary>A window of the board centred on the caller.</summary>
public class LeaderboardNeighbourhoodDto
{
    public Guid CycleId { get; set; }

    public string Cohort { get; set; } = string.Empty;

    public IReadOnlyList<LeaderboardEntryDto> Entries { get; set; } = [];

    public LeaderboardStandingDto Standing { get; set; } = new();

    public DateTime ServerTimeUtc { get; set; }
}

/// <summary>What the caller may change about how they appear.</summary>
public class LeaderboardVisibilityRequest
{
    public bool IsHidden { get; set; }
}

public class LeaderboardVisibilityDto
{
    public string DisplayName { get; set; } = string.Empty;

    public bool IsHidden { get; set; }

    /// <summary>
    /// True when a guardian set this, in which case the player's own toggle is refused. The client
    /// renders an explanation instead of a control.
    /// </summary>
    public bool IsLockedByGuardian { get; set; }
}

/// <summary>
/// Where the caller finished a settled cycle, and what it paid.
/// <para>
/// The currency itself arrives through the ordinary balance path — this route only explains it, so
/// a results screen can say "3rd place, 50 coins" without inventing a second way for money to
/// reach a player.
/// </para>
/// </summary>
public class LeaderboardSettlementDto
{
    public Guid CycleId { get; set; }

    public string Cohort { get; set; } = string.Empty;

    public int FinalRank { get; set; }

    public long Value { get; set; }

    /// <summary>The prize band that paid, e.g. <c>top10</c>. Null when the rank was outside every band.</summary>
    public string? RewardBand { get; set; }

    public bool RewardIssued { get; set; }

    public DateTime? RewardIssuedAtUtc { get; set; }

    public DateTime SettledAtUtc { get; set; }
}
