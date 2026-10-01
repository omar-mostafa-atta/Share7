namespace Share7.Application.Multiplayer.Models;

/// <summary>Where a match's result stands, as the client sees it.</summary>
public enum MatchResultStatus
{
    Unknown = 0,

    /// <summary>Not decided yet — the match has not started, or results are still arriving. Poll again.</summary>
    Pending,

    /// <summary>Placed by the mode's win rule.</summary>
    Decided,

    /// <summary>The mode has no win rule: who played is recorded, nobody is placed.</summary>
    Unranked,

    /// <summary>Voided by an operator.</summary>
    Void
}

/// <summary>One step of a win rule, as a client or a console renders it.</summary>
public class MatchWinCriterionDto
{
    /// <summary><c>correct_answers</c>, <c>accuracy</c>, <c>duration_ms</c>, <c>outcome</c>, or <c>signal:&lt;kind&gt;</c>.</summary>
    public string Metric { get; set; } = string.Empty;

    /// <summary><c>higher</c> or <c>lower</c> — which way wins.</summary>
    public string Order { get; set; } = string.Empty;

    /// <summary>
    /// <c>verified</c>, <c>bounded</c> or <c>reported</c>: how far the server can vouch for this
    /// metric. Read-only; ignored on a save.
    /// </summary>
    public string? Trust { get; set; }
}

/// <summary>One participant's line on a results screen.</summary>
public class MatchPlacementDto
{
    public int Slot { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The player's public handle — never a real name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>1 is first; ties share a placement. Null while pending, or when the mode ranks nobody.</summary>
    public int? Placement { get; set; }

    public bool IsWinner { get; set; }

    /// <summary>Whether this player's result has reached the server. What a waiting screen counts.</summary>
    public bool Reported { get; set; }

    /// <summary>Nothing arrived before the verdict; placed after everyone who reported.</summary>
    public bool Forfeited { get; set; }

    /// <summary>Reported past what is physically possible; placed after every clean player.</summary>
    public bool Flagged { get; set; }

    /// <summary>
    /// The value of each metric in the rule, keyed by metric token — what the results screen shows
    /// next to the name ("8 correct · 41.2 s"). Empty while pending.
    /// </summary>
    public Dictionary<string, long> Values { get; set; } = [];
}

/// <summary>
/// The server's verdict on a match — or where it stands if there is none yet.
/// <para>
/// <b>Nobody's client decided this.</b> Placements come from each player's own settled run and
/// graded answers, under the mode's win rule as it was when the match was decided.
/// </para>
/// </summary>
public class MatchResultDto
{
    public Guid SessionId { get; set; }

    public MatchResultStatus State { get; set; }

    /// <summary><c>all_reported</c> or <c>deadline</c>. Null while pending.</summary>
    public string? DecidedBy { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>The rule this match is (or will be) decided by. Empty for a mode with no rule.</summary>
    public List<MatchWinCriterionDto> Rule { get; set; } = [];

    /// <summary>Everyone who held a seat when the match started, best placement first, then by seat.</summary>
    public List<MatchPlacementDto> Placements { get; set; } = [];

    /// <summary>
    /// For a rated match only, and only the caller's own: what it did to their visible rank. Null
    /// otherwise, or while the result is pending.
    /// </summary>
    public RankedResultDto? Ranked { get; set; }

    public DateTime ServerTimeUtc { get; set; }
}

/// <summary>One thing a mode's win rule may rank on, for an authoring screen.</summary>
public class MatchMetricOptionDto
{
    public string Metric { get; set; } = string.Empty;

    /// <summary><c>verified</c>, <c>bounded</c> or <c>reported</c>.</summary>
    public string Trust { get; set; } = string.Empty;

    /// <summary><c>answers</c> — graded attempts sent with the session id — or <c>run</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The order that usually makes sense for it, so a form can pre-select it.</summary>
    public string SuggestedOrder { get; set; } = string.Empty;
}
