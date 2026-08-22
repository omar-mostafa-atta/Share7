namespace Share7.Domain.Multiplayer;

/// <summary>
/// Why a session ended. Recorded on every terminal transition, because "the match vanished" is a
/// support question and the answer has to be in the row rather than inferred from timestamps.
/// </summary>
public enum SessionClosedReason
{
    Unknown = 0,

    /// <summary>The host closed it deliberately.</summary>
    HostClosed,

    /// <summary>Swept: the host stopped heartbeating for longer than <c>SessionTimeoutSeconds</c>.</summary>
    Abandoned,

    /// <summary>The last member left. Nothing went wrong.</summary>
    Empty,

    /// <summary>Swept: stuck in <c>Creating</c>, so the transport room never came up.</summary>
    CreationFailed,

    /// <summary>Forced closed from the admin surface.</summary>
    AdminClosed
}
