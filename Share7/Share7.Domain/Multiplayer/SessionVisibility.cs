namespace Share7.Domain.Multiplayer;

/// <summary>
/// Whether matchmaking is allowed to hand this session to a stranger.
/// <para>
/// <see cref="Private"/> sessions are never returned as matchmaking candidates and are reachable
/// only by their join code — that is the whole difference, and it is enforced in the candidate
/// query rather than by hiding the row.
/// </para>
/// </summary>
public enum SessionVisibility
{
    Unknown = 0,
    Public,
    Private
}
