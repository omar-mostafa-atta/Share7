namespace Share7.Domain.Multiplayer;

/// <summary>
/// Where a session is in its life. **The server owns this** — the client mirrors the same machine
/// so it can refuse an impossible move locally, but a disagreement is always resolved in favour of
/// what is stored here.
/// <para>
/// Stored as text, like <c>TransactionState</c>: a state token appears in logs and support tickets,
/// and reordering the enum must not silently re-map rows that already exist.
/// </para>
/// <para>
/// <c>Unknown = 0</c> is the deliberate default, because <c>WireEnum.FromWire</c> falls back to
/// <c>default</c> for a value it does not recognise — a row written by a newer deployment must read
/// as obviously-unknown on an older one, never as a plausible state like <c>Creating</c>.
/// </para>
/// </summary>
public enum MultiplayerSessionState
{
    Unknown = 0,

    /// <summary>Record exists; the host has not yet confirmed the transport room came up.</summary>
    Creating,

    /// <summary>Transport room confirmed, waiting for players. **The only joinable state.**</summary>
    Created,

    /// <summary>Host committed to start. Joins are closed from here on.</summary>
    Starting,

    /// <summary>Gameplay in progress.</summary>
    Running,

    /// <summary>Gameplay finished, results in flight on the client.</summary>
    Ending,

    /// <summary>Teardown begun.</summary>
    Closing,

    /// <summary>Terminal, clean.</summary>
    Closed,

    /// <summary>Terminal. Creation or start never completed.</summary>
    Failed,

    /// <summary>Terminal. Swept away after the host stopped heartbeating.</summary>
    Abandoned
}

/// <summary>Helpers over <see cref="MultiplayerSessionState"/> that several services need to agree on.</summary>
public static class MultiplayerSessionStates
{
    /// <summary>
    /// States a session never leaves. The filtered unique indexes are defined against exactly this
    /// set — a terminal session releases its transport name and join code for reuse.
    /// </summary>
    public static readonly IReadOnlySet<MultiplayerSessionState> Terminal =
        new HashSet<MultiplayerSessionState>
        {
            MultiplayerSessionState.Closed,
            MultiplayerSessionState.Failed,
            MultiplayerSessionState.Abandoned
        };

    public static bool IsTerminal(this MultiplayerSessionState state) => Terminal.Contains(state);
}
