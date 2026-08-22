using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// Find a session to play, or start one.
/// <para>
/// **No queue, no worker, no distributed lock.** Candidates are selected with one indexed read and
/// then joined through the same race-proof seating path a direct join uses; a candidate that fills
/// between selection and join simply fails its conditional UPDATE and the loop moves to the next.
/// That retry loop is the entire race defence, and it is enough because the join underneath it is
/// already atomic.
/// </para>
/// </summary>
public interface IMatchmakingService
{
    Task<ServiceResult<MatchmakeResponse>> MatchmakeAsync(
        Guid userId,
        MatchmakeRequest request,
        CancellationToken cancellationToken = default);
}
