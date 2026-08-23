using Share7.Application.Common.Models;
using Share7.Application.Leaderboards.Models;

namespace Share7.Application.Leaderboards.Interfaces;

/// <summary>
/// Everything the game client may ask about ranking. **All of it is reads.**
/// <para>
/// There is no submit method here and there must never be one. A route that accepted a
/// client-stated score would make a modified build the author of its own rank, which is the exact
/// failure the purchase path avoids by taking an <c>OfferId</c> instead of a price.
/// </para>
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// Boards on offer to this caller, each with its live cycle. Cohorts are already filtered to
    /// the ones the caller actually belongs to.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<LeaderboardBoardDto>>> GetBoardsAsync(
        Guid userId, Guid? gameId, CancellationToken cancellationToken = default);

    /// <summary>A board's cycle history, newest first.</summary>
    Task<ServiceResult<IReadOnlyList<LeaderboardCycleDto>>> GetCyclesAsync(
        Guid boardId, int limit, CancellationToken cancellationToken = default);

    /// <summary>One page of a cycle, resumed from an opaque cursor.</summary>
    Task<ServiceResult<LeaderboardPageDto>> GetPageAsync(
        Guid userId, Guid cycleId, string? cohort, string? cursor, int? limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The rows immediately around the caller. The interesting view for anyone who is not in the
    /// top ten, which is nearly everyone.
    /// </summary>
    Task<ServiceResult<LeaderboardNeighbourhoodDto>> GetAroundMeAsync(
        Guid userId, Guid cycleId, string? cohort, int? window,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's own standing, and nothing else.</summary>
    Task<ServiceResult<LeaderboardStandingDto>> GetStandingAsync(
        Guid userId, Guid cycleId, string? cohort, CancellationToken cancellationToken = default);

    /// <summary>How the caller currently appears, including whether a guardian has locked it.</summary>
    Task<ServiceResult<LeaderboardVisibilityDto>> GetVisibilityAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Opt in or out of public listing. Refused when a guardian has forced it.</summary>
    Task<ServiceResult<LeaderboardVisibilityDto>> SetVisibilityAsync(
        Guid userId, bool isHidden, CancellationToken cancellationToken = default);
}

/// <summary>
/// Board authoring, for operators. Not reachable by the game client.
/// <para>
/// Adding a leaderboard is an INSERT through this interface — no migration, no deploy, no client
/// release. That is the whole reason boards are data.
/// </para>
/// </summary>
public interface ILeaderboardAdminService
{
    Task<ServiceResult<IReadOnlyList<LeaderboardBoardAdminDto>>> GetBoardsAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<LeaderboardBoardAdminDto>> CreateBoardAsync(
        SaveLeaderboardBoardRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a board's presentation and policy. Key, metric and aggregation are deliberately
    /// immutable — changing any of them would silently alter what the existing entries mean.
    /// </summary>
    Task<ServiceResult<LeaderboardBoardAdminDto>> UpdateBoardAsync(
        Guid boardId, SaveLeaderboardBoardRequest request, CancellationToken cancellationToken = default);

    /// <summary>Authors a window by hand, for event boards whose bounds are not derived.</summary>
    Task<ServiceResult> CreateEventCycleAsync(
        Guid boardId, CreateLeaderboardCycleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws a cycle's entries away and replays them from <c>GameResults</c>. The recovery path,
    /// and the proof that a rank is nothing more than a function of the results behind it.
    /// </summary>
    Task<ServiceResult> RebuildCycleAsync(Guid cycleId, CancellationToken cancellationToken = default);
}
