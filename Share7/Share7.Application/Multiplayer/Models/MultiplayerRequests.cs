using System.ComponentModel.DataAnnotations;
using Share7.Domain.Multiplayer;

namespace Share7.Application.Multiplayer.Models;

/// <summary>
/// The idempotency key every mutating multiplayer request may carry.
/// <para>
/// **Optional, and a missing one is never an error.** A caller that omits it gets a
/// server-generated key, which makes the call work but leaves the retry unprotected — a generated
/// key is new every time, so a repeated call is a genuinely new operation. Clients that can retry
/// should mint one per operation and reuse it for every retry of *that* operation.
/// </para>
/// <para>
/// A key is only consumed by an operation that **succeeded**. A join refused because the session was
/// full leaves the key unspent, so the obvious client behaviour — retry the same key when a seat
/// frees up — works instead of replaying the refusal forever.
/// </para>
/// </summary>
public abstract class MultiplayerRequest
{
    [MaxLength(128)]
    public string? RequestId { get; set; }
}

public class CreateMultiplayerSessionRequest : MultiplayerRequest
{
    public Guid GameId { get; set; }

    /// <summary>
    /// The Photon room name, minted client-side. Must be unique among live sessions; a collision is
    /// answered <c>TRANSPORT_NAME_TAKEN</c> by the database rather than by a lookup here.
    /// </summary>
    [MaxLength(64)]
    public string TransportSessionName { get; set; } = string.Empty;

    [MaxLength(16)]
    public string? TransportRegion { get; set; }

    /// <summary>Defaults to <see cref="SessionVisibility.Public"/> when omitted.</summary>
    public SessionVisibility? Visibility { get; set; }

    /// <summary>
    /// Optional, and **clamped to the game's own maximum** — a client asking for more seats than the
    /// catalog allows gets the catalog's number rather than a refusal, because the catalog is
    /// authoritative and the request is a preference.
    /// </summary>
    public int? MaxPlayers { get; set; }

    public bool IsRanked { get; set; }

    /// <summary>
    /// Required in practice: an omitted value arrives as <c>0</c>, which is not an accepted version,
    /// so it is refused with <c>PROTOCOL_VERSION_MISMATCH</c> — the right answer for a client that
    /// does not declare one.
    /// </summary>
    public int ProtocolVersion { get; set; }

    public CurriculumPathDto? CurriculumPath { get; set; }
}

public class JoinMultiplayerSessionRequest : MultiplayerRequest
{
    public int ProtocolVersion { get; set; }
}

public class LeaveMultiplayerSessionRequest : MultiplayerRequest;

public class StartMultiplayerSessionRequest : MultiplayerRequest;

public class CloseMultiplayerSessionRequest : MultiplayerRequest
{
    /// <summary>
    /// Why the host is closing. Only <see cref="SessionClosedReason.HostClosed"/> and
    /// <see cref="SessionClosedReason.Empty"/> are honoured from a client; anything else is recorded
    /// as <c>HostClosed</c>, because a client cannot truthfully assert that it was swept or closed by
    /// an admin.
    /// </summary>
    public SessionClosedReason? Reason { get; set; }
}

/// <summary>
/// The host checking in, and reporting who it can actually see on the transport.
/// <para>
/// **This is the host asserting presence, never membership.** A user id here that is not already a
/// member is ignored and logged — the roster cannot seat anybody, or a compromised client could add
/// players to a session by naming them.
/// </para>
/// </summary>
public class HeartbeatRequest : MultiplayerRequest
{
    /// <summary>The host's live realtime roster. Members it omits are treated as missing, not gone.</summary>
    public List<Guid> ConnectedUserIds { get; set; } = [];

    /// <summary>
    /// The host's own view of the session state, for drift detection. **Advisory only** — it is
    /// recorded in logs and never acted on; the response carries the authoritative state back.
    /// </summary>
    public MultiplayerSessionState? State { get; set; }
}

/// <summary>
/// Who should hold authority next.
/// <para>
/// **The one request in this domain that names a user, and it is a target rather than an identity.**
/// The caller is still the token's subject; <see cref="ToUserId"/> only says who authority should
/// move to. An involuntary claim — made when the current host has gone quiet — may only ever name
/// the caller themselves, so it cannot be used to install a third party.
/// </para>
/// </summary>
public class TransferHostRequest : MultiplayerRequest
{
    public Guid ToUserId { get; set; }

    public HostTransferReason? Reason { get; set; }
}

/// <summary>Why authority is moving. Advisory — the server decides which rule actually applies.</summary>
public enum HostTransferReason
{
    Unknown = 0,

    /// <summary>The current host is handing over deliberately.</summary>
    Voluntary,

    /// <summary>A member is claiming a host that has stopped responding.</summary>
    HostUnreachable
}

/// <summary>
/// Find a session to play, or start one.
/// <para>
/// Deliberately one call rather than a queue. At this scale the candidate set is small, the join is
/// already race-proof, and a retry loop over a handful of rows needs no distributed lock, no worker
/// and no Redis — the failure mode of a matchmaking queue is far worse than the cost of a few
/// extra reads.
/// </para>
/// </summary>
public class MatchmakeRequest : MultiplayerRequest
{
    public Guid GameId { get; set; }

    public int ProtocolVersion { get; set; }

    public bool IsRanked { get; set; }

    public int? MaxPlayers { get; set; }

    /// <summary>
    /// Optional filter. Only <c>lessonId</c> narrows the search — agreed with the Unity dev as the
    /// v1 rule. The rest of the path is carried onto a session this call creates, and ignored when
    /// joining an existing one.
    /// </summary>
    public CurriculumPathDto? CurriculumPath { get; set; }

    /// <summary>Defaults to true. When false, a fruitless search reports <c>NoMatch</c> instead.</summary>
    public bool CreateIfNoneFound { get; set; } = true;

    /// <summary>Required when a session may be created — the room name to use if it comes to that.</summary>
    [MaxLength(64)]
    public string? TransportSessionName { get; set; }

    [MaxLength(16)]
    public string? TransportRegion { get; set; }
}

/// <summary>Filters for "which sessions am I in?" — every field optional, all of them ANDed.</summary>
public class MultiplayerSessionQuery
{
    public Guid? GameId { get; set; }
    public MultiplayerSessionState? State { get; set; }
    public SessionVisibility? Visibility { get; set; }
    public bool? IsRanked { get; set; }
    public Guid? LessonId { get; set; }
}
