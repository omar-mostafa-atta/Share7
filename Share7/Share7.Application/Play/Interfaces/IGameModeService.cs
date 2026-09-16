using Share7.Application.Play.Models;

namespace Share7.Application.Play.Interfaces;

/// <summary>
/// The read side of the mode catalogue, as the Unity client sees it.
/// <para>
/// Anonymous by design: the mode picker is reachable before a session exists, and entitlement state
/// is not returned here because the client already holds its own entitlements.
/// </para>
/// </summary>
public interface IGameModeService
{
    /// <summary>
    /// The modes one game offers right now, newest policy wins.
    /// <para>
    /// A <paramref name="gameKey"/> this server has never heard of answers with an empty list rather
    /// than a 404: a client asking about a game only its own content catalogue knows is an
    /// offline-content build, which is a supported state and not an error.
    /// </para>
    /// </summary>
    /// <param name="includeScheduled">
    /// Include modes whose window has not opened yet, so a picker can show "opens Friday". Withdrawn
    /// modes are omitted either way — inactive is an operator decision, not a schedule.
    /// </param>
    Task<GameModesResponse> GetForGameAsync(
        string gameKey, bool includeScheduled = false, CancellationToken cancellationToken = default);
}
