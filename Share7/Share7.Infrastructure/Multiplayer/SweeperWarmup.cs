namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// When this process started taking multiplayer traffic, so the sweeper can tell "the host went
/// quiet" from "the host could not reach us because we were down".
/// <para>
/// **Every time-based sweep rule measures silence, and a restart manufactures silence.** A deploy on
/// shared IIS — cold start, migrations — can take minutes, and during them no heartbeat can land. Left
/// alone, the first pass after the restart would read every live session as abandoned and end them
/// all, and the hosts, told <c>Abandoned</c>, would tear down matches that were playing perfectly well
/// over the transport. So the silence rules wait until this process has been reachable for one full
/// window of the longest timeout; a genuinely dead session lingers that much longer after a restart,
/// which costs nothing anyone can see.
/// </para>
/// <para>
/// Per process, which is exact for the single instance Share7 runs today. With several instances
/// behind a balancer the others keep taking heartbeats through a rolling restart, so a restarted
/// instance waiting is merely redundant, never wrong.
/// </para>
/// </summary>
public sealed class SweeperWarmup
{
    public SweeperWarmup() : this(DateTime.UtcNow) { }

    public SweeperWarmup(DateTime startedAtUtc) => StartedAtUtc = startedAtUtc;

    public DateTime StartedAtUtc { get; }

    /// <summary>
    /// Whether silence of up to <paramref name="window"/> could be this process's own downtime rather
    /// than the client's — true until the process has been up for the whole window.
    /// </summary>
    public bool IsWarmingUp(DateTime nowUtc, TimeSpan window) => nowUtc - StartedAtUtc < window;
}
