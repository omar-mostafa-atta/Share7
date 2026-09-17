using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// Operator tooling for the 3 AM page. Read-mostly — one mutation, and it is a forced close.
/// <para>
/// **Not scoped to a caller.** Every other read in this domain answers only for sessions the caller
/// is in; these answer for all of them, which is precisely why the routes sit behind the admin role
/// on a separate controller rather than as a flag on the player-facing ones. A privilege check that
/// lives in a parameter is one that eventually gets passed the wrong value.
/// </para>
/// </summary>
public interface IMultiplayerAdminService
{
    /// <summary>Sessions matching the filter, newest first, capped.</summary>
    Task<ServiceResult<MultiplayerAdminSessionsDto>> ListAsync(
        MultiplayerAdminQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The roster of any session, member or not.
    /// <para>
    /// Returns **departed members too**, unlike the player-facing roster. Who left and when is the
    /// substance of most support questions, and an operator looking at a closed session would
    /// otherwise be shown an empty list.
    /// </para>
    /// </summary>
    Task<ServiceResult<IReadOnlyList<MultiplayerSessionPlayerDto>>> GetPlayersAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a session closed, recording <c>AdminClosed</c>.
    /// <para>
    /// Idempotent and absorbing, like every other close: a session that already ended keeps the
    /// reason and the time it ended with. **An operator can end a match, but cannot rewrite the
    /// history of one that already finished.**
    /// </para>
    /// </summary>
    Task<ServiceResult<MultiplayerSessionSummaryDto>> CloseAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
