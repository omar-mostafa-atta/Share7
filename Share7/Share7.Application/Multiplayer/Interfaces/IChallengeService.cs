using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// Challenges between classmates and friends: asynchronous ("beat my score by Friday"), decided by
/// the server from graded attempts, and live (a private room reserved for the two, with an invite).
/// Who may challenge whom is <c>ISocialPolicy</c>'s decision.
/// </summary>
public interface IChallengeService
{
    /// <summary>
    /// Sends a "beat my score" challenge. The bar is the sender's best on the lesson in that game.
    /// Sending it again while it is open returns the same challenge. Refusals:
    /// <c>SOCIAL_NOT_ALLOWED</c>, <c>CHALLENGE_NO_SCORE</c>, <c>CHALLENGE_LESSON_LOCKED</c>,
    /// <c>VALIDATION_FAILED</c>.
    /// </summary>
    Task<ServiceResult<ChallengeDto>> SendAsync(Guid userId, SendChallengeRequest request, CancellationToken cancellationToken = default);

    /// <summary>The caller's challenges, sent and received: every open one, and those ended in the last week.</summary>
    Task<ServiceResult<IReadOnlyList<ChallengeDto>>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The recipient takes it on. Only attempts graded after this count.</summary>
    Task<ServiceResult<ChallengeDto>> AcceptAsync(Guid userId, Guid challengeId, CancellationToken cancellationToken = default);

    /// <summary>The recipient says no. The challenger is not notified.</summary>
    Task<ServiceResult<ChallengeDto>> DeclineAsync(Guid userId, Guid challengeId, CancellationToken cancellationToken = default);

    /// <summary>The challenger withdraws it — only before it is accepted, so nobody can pull a challenge they are losing.</summary>
    Task<ServiceResult<ChallengeDto>> CancelAsync(Guid userId, Guid challengeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A live challenge: a private room reserved for the two of them, hosted by the caller, and an
    /// invite to the other. Bring the room up and <c>start</c> it as after any create.
    /// </summary>
    Task<ServiceResult<LiveChallengeDto>> SendLiveAsync(Guid userId, SendLiveChallengeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decides every challenge that is due: beaten, or past its deadline. Run by the sweeper; the
    /// reads settle the caller's own challenges as well, so a list is never stale.
    /// </summary>
    Task<int> SettleDueAsync(CancellationToken cancellationToken = default);
}
