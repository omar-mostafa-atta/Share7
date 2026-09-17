using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// The session lifecycle: create, confirm, start, join, leave, close, read.
/// <para>
/// **Every method takes the caller's id as its first argument and no request body carries one.**
/// That is what makes the three impersonation attacks — spoofing a user id, impersonating the host,
/// joining on another child's behalf — structurally impossible rather than merely validated against.
/// </para>
/// </summary>
public interface IMultiplayerSessionService
{
    /// <summary>
    /// Creates a session in <c>Creating</c> together with its host's membership, in one transaction.
    /// **A session can never exist without its host.**
    /// </summary>
    Task<ServiceResult<MultiplayerSessionDto>> CreateAsync(
        Guid userId,
        CreateMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Serves both forward transitions, chosen by the state the session is actually in:
    /// <list type="bullet">
    /// <item><c>Creating → Created</c> — the host confirms the transport room came up. This is the
    /// confirmation half of the create saga; until it happens nobody can join.</item>
    /// <item><c>Created → Running</c> — the host commits to start. Joins close and the clock starts.</item>
    /// </list>
    /// One endpoint rather than two because the client's decision is the same in both cases ("I am
    /// ready"), and because a client that has lost track of which half it is on would otherwise have
    /// to guess.
    /// </summary>
    Task<ServiceResult<MultiplayerSessionDto>> StartAsync(
        Guid userId,
        Guid sessionId,
        StartMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Seats the caller, if there is room. Capacity is decided by a single conditional UPDATE, so two
    /// clients racing for the last seat cannot both win.
    /// </summary>
    Task<ServiceResult<MultiplayerSessionDto>> JoinAsync(
        Guid userId,
        Guid sessionId,
        JoinMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the caller's seat. **Idempotent**: leaving twice, or leaving a session that has
    /// already ended, succeeds and reports the session as it stands.
    /// <para>
    /// If the leaver was the host, authority passes to the lowest-slot remaining member; if nobody
    /// remains, the session closes as <c>Empty</c>.
    /// </para>
    /// </summary>
    Task<ServiceResult<MultiplayerSessionDto>> LeaveAsync(
        Guid userId,
        Guid sessionId,
        LeaveMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session. Host only. **Idempotent and absorbing** — on an already-terminal session it
    /// succeeds and returns the existing record without moving <c>EndedAtUtc</c> or overwriting the
    /// reason it originally ended for.
    /// </summary>
    Task<ServiceResult<MultiplayerSessionDto>> CloseAsync(
        Guid userId,
        Guid sessionId,
        CloseMultiplayerSessionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One session, for a member of it.
    /// <para>
    /// **A non-member gets 404, not 403.** 403 would confirm that the id exists, which is enough to
    /// enumerate live sessions; 404 tells someone who is not in a session exactly as much as they
    /// are entitled to know.
    /// </para>
    /// </summary>
    Task<ServiceResult<MultiplayerSessionDto>> GetAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The roster — what the host maps onto its realtime peers. Members only, same 404 rule.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<MultiplayerSessionPlayerDto>>> GetPlayersAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The host checking in. Host only.
    /// <para>
    /// Writes <c>LastHeartbeatAtUtc</c> from the **server clock** — never from anything the client
    /// sends. A device with a skewed or edited clock could otherwise keep a dead session alive
    /// indefinitely, or kill a live one.
    /// </para>
    /// <para>
    /// A former host gets 403 here, and that refusal is the whole mechanism that stops a host which
    /// dropped out and came back from resurrecting a session it no longer owns.
    /// </para>
    /// </summary>
    Task<ServiceResult<HeartbeatResponse>> HeartbeatAsync(
        Guid userId,
        Guid sessionId,
        HeartbeatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves authority. Accepted from the current host at any time, or from any member once the
    /// current host has been unseen for <c>HostClaimGraceSeconds</c> — in which case the claim may
    /// only name the caller.
    /// <para>
    /// Guarded on <c>RowVersion</c>, so simultaneous claims produce exactly one winner. The loser
    /// re-reads and sees who won rather than retrying blindly into a second migration.
    /// </para>
    /// </summary>
    Task<ServiceResult<MultiplayerSessionDto>> TransferHostAsync(
        Guid userId,
        Guid sessionId,
        TransferHostRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sessions the caller is a member of.
    /// <para>
    /// **A recovery route, not a lobby browser** — "where am I?" after a crash or a reinstall. Photon
    /// serves discovery from its own room list, so nothing here exposes other people's sessions.
    /// Returns an empty list when the caller is in none.
    /// </para>
    /// </summary>
    Task<ServiceResult<IReadOnlyList<MultiplayerSessionDto>>> ListForUserAsync(
        Guid userId,
        MultiplayerSessionQuery query,
        CancellationToken cancellationToken = default);
}
