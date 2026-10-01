namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>What one sweep pass actually did. Returned so the pass is observable and testable.</summary>
/// <param name="OrphanedSeatsReleased">
/// Seats still held in sessions that had already ended — released by the healing rule. Should be zero
/// in steady state; anything else means a close was interrupted between its two writes (a crash, a
/// killed app pool) or rows predate the rule, and is worth a look.
/// </param>
/// <param name="MatchesDecided">Matches whose result this pass decided.</param>
public record MultiplayerSweepResult(
    int FailedCreating,
    int Abandoned,
    int ClosedEmpty,
    int PlayersReleased,
    int RequestLogsPurged,
    int OrphanedSeatsReleased = 0,
    int MatchesDecided = 0,
    int EventsPurged = 0,
    int InvitationsExpired = 0,
    int ChallengesSettled = 0,
    int TournamentsAdvanced = 0)
{
    public static readonly MultiplayerSweepResult Empty = new(0, 0, 0, 0, 0);

    public int Total =>
        FailedCreating + Abandoned + ClosedEmpty + PlayersReleased + RequestLogsPurged + OrphanedSeatsReleased
        + MatchesDecided + EventsPurged + InvitationsExpired + ChallengesSettled + TournamentsAdvanced;
}

/// <summary>
/// The janitor. Everything that cleans up after a client that stopped talking lives here.
/// <para>
/// **This is the only thing that makes a crashed host recoverable.** Every failure mode in this
/// domain — the host force-quits, the phone dies, the transport room never comes up — ends with rows
/// nobody will ever come back to close. Without a sweep those sessions hold their transport names
/// forever and their members can never join anything else, because one account plays one match at a
/// time.
/// </para>
/// <para>
/// Deliberately a **scoped service rather than logic inside the background worker**, so a test can
/// run exactly one pass and assert on it without standing up a host. The worker is a timer and
/// nothing else.
/// </para>
/// </summary>
public interface IMultiplayerSweepService
{
    /// <summary>
    /// One pass. **Idempotent and batched**, so overlapping runs across instances are harmless and a
    /// backlog can never hold one long transaction open.
    /// </summary>
    Task<MultiplayerSweepResult> SweepAsync(CancellationToken cancellationToken = default);
}
