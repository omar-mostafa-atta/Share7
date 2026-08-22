namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>What one sweep pass actually did. Returned so the pass is observable and testable.</summary>
public record MultiplayerSweepResult(
    int FailedCreating,
    int Abandoned,
    int ClosedEmpty,
    int PlayersReleased,
    int RequestLogsPurged)
{
    public static readonly MultiplayerSweepResult Empty = new(0, 0, 0, 0, 0);

    public int Total => FailedCreating + Abandoned + ClosedEmpty + PlayersReleased + RequestLogsPurged;
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
