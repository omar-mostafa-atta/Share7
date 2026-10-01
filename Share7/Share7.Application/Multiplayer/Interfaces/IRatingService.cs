using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>One player's line of a decided match, as the rating update needs it.</summary>
public sealed record RatedPlacement(Guid UserId, int Placement, bool IsWinner, bool Forfeited = false);

/// <summary>
/// Ranked: hidden ratings that decide who meets whom, and the visible seasonal rank they project to.
/// </summary>
public interface IRatingService
{
    /// <summary>
    /// Rates a decided match — only a rated session, only once (keyed by the match), inside the
    /// caller's transaction (the verdict's), with players locked in user order. Forfeits arrive as
    /// last place and count as losses. Players over the repeat-opponent limit are recorded but not
    /// moved. Returns the players who reached a new visible tier this season, and the tier (1 bronze
    /// to 5 diamond) — what the verdict records as <c>RANKED_TIER</c> for season quests.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> ApplyAsync(Guid sessionId, Guid modeId, IReadOnlyList<RatedPlacement> placements, CancellationToken cancellationToken = default);

    /// <summary>Hidden ratings for a pool — what ticket matchmaking compares. New players read as the initial rating.</summary>
    Task<IReadOnlyDictionary<Guid, Domain.Multiplayer.Rating>> RatingsAsync(
        IReadOnlyCollection<Guid> userIds, Guid modeId, CancellationToken cancellationToken = default);

    /// <summary>The caller's visible standing in a ranked mode this season.</summary>
    Task<ServiceResult<RankedStandingDto>> StandingAsync(Guid userId, Guid modeId, CancellationToken cancellationToken = default);

    /// <summary>What a rated match did to the caller's standing, for the results screen; null if it was not rated for them.</summary>
    Task<RankedResultDto?> ResultForAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
}
