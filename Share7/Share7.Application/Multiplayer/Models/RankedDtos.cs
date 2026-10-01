namespace Share7.Application.Multiplayer.Models;

/// <summary>
/// A player's visible rank in one mode this season. The hidden rating behind it is never sent.
/// </summary>
public class RankedStandingDto
{
    public Guid ModeId { get; set; }

    /// <summary><c>yyyy-MM</c>: seasons are calendar months, UTC.</summary>
    public string SeasonKey { get; set; } = string.Empty;

    public DateTime SeasonEndsAtUtc { get; set; }

    /// <summary>True until the season's placement matches are played; no tier is shown until then.</summary>
    public bool IsPlacement { get; set; }

    public int PlacementMatchesLeft { get; set; }

    /// <summary><c>bronze</c>, <c>silver</c>, <c>gold</c>, <c>platinum</c>, <c>diamond</c>; null while placing.</summary>
    public string? Tier { get; set; }

    /// <summary>3 is the lowest division of a tier and 1 the highest; null for diamond, or while placing.</summary>
    public int? Division { get; set; }

    public int MatchesPlayed { get; set; }
    public int Wins { get; set; }

    public DateTime ServerTimeUtc { get; set; }
}

/// <summary>What one rated match did to the caller's visible standing.</summary>
public class RankedResultDto
{
    public Guid ModeId { get; set; }
    public string SeasonKey { get; set; } = string.Empty;
    public bool IsPlacement { get; set; }
    public int PlacementMatchesLeft { get; set; }
    public string? Tier { get; set; }
    public int? Division { get; set; }

    /// <summary>This match took them to a higher division or tier than they had reached this season.</summary>
    public bool Promoted { get; set; }

    /// <summary>
    /// The rating did not move for this match, and why — <c>repeat_opponent</c> when the same players
    /// have met too often today. Shown so a child is not left wondering why a win "did nothing".
    /// </summary>
    public string? NotCountedReason { get; set; }
}
