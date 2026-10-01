using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// Decides matches, from what the players' own results show.
/// <para>
/// <b>A match is decided the moment it can be</b> — as soon as every participant's result is in, or
/// once the match has ended and <c>MatchResultGraceSeconds</c> has passed, at which point anyone still
/// missing forfeits. Either a read or the sweeper can be the one to decide it; the result's key
/// makes sure only one verdict is ever written.
/// </para>
/// <para>
/// Nothing here pays anything or knows what a win is worth. A decided match raises
/// <c>MATCHES_PLAYED</c> and <c>MATCHES_WON</c> into the game-results stream, and the boards and
/// quests that already consume that stream decide the rest.
/// </para>
/// </summary>
public interface IMatchResultService
{
    /// <summary>
    /// The result of a match, for someone who held a seat in it — deciding it first if it has become
    /// decidable. A stranger, or a player the host removed, gets <c>SESSION_NOT_FOUND</c>.
    /// </summary>
    Task<ServiceResult<MatchResultDto>> GetAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides every recent match that has become decidable. Driven by the sweeper, so a match is
    /// decided even if nobody asks. <paramref name="allowDeadline"/> is false while the process is
    /// warming up: a forfeit for silence that was really this server's downtime would be unfair.
    /// </summary>
    Task<int> DecideDueAsync(bool allowDeadline, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a win rule for a mode of <paramref name="gameId"/> may rank on: the fixed metrics, and a
    /// <c>signal:&lt;kind&gt;</c> for every count the game or the platform knows about.
    /// </summary>
    Task<IReadOnlyList<MatchMetricOptionDto>> MetricOptionsAsync(
        Guid? gameId,
        CancellationToken cancellationToken = default);
}
