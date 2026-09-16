using Share7.Domain.Games;

namespace Share7.Domain.Multiplayer;

/// <summary>
/// One multiplayer match, from the moment a host asks for it to the moment it goes terminal.
/// <para>
/// **The backend is not in the gameplay loop.** Photon Fusion owns the realtime connection; this row
/// is the arbiter of everything Photon cannot settle on its own — who is allowed in, how many fit,
/// who is host, and whether the session still exists at all. If this row and the transport disagree,
/// this row wins for every question of membership and authority.
/// </para>
/// </summary>
public class MultiplayerSession
{
    public Guid Id { get; set; }

    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>
    /// Who currently holds authority. **Moves on migration** — see the host-transfer path. The old
    /// host's next heartbeat is refused precisely because this changed underneath it, which is what
    /// stops a returning stale host from resurrecting a session it no longer owns.
    /// </summary>
    public Guid HostUserId { get; set; }

    /// <summary>
    /// The Photon room name, minted by the client.
    /// <para>
    /// **Deliberately not named <c>PhotonSessionName</c>**: the column outlives the vendor. Unique
    /// among non-terminal sessions by filtered index — two concurrent creates that mint the same
    /// name cannot both commit, and a terminal session releases the name for reuse.
    /// </para>
    /// </summary>
    public string TransportSessionName { get; set; } = string.Empty;

    /// <summary>Photon region token, or null for "best available".</summary>
    public string? TransportRegion { get; set; }

    /// <summary>
    /// Human-typable code for a private session. Unique among non-terminal sessions when non-null,
    /// which is why the index carries both conditions — a null code is not a collision.
    /// </summary>
    public string? JoinCode { get; set; }

    public MultiplayerSessionState State { get; set; }

    public SessionVisibility Visibility { get; set; }

    /// <summary>
    /// Copied from <see cref="Game.MaxPlayers"/> at creation, **not read through to it**. The catalog
    /// row can be edited while a match is live and must not retroactively resize it — a session that
    /// seated four players cannot become a three-player session underneath them.
    /// </summary>
    public int MaxPlayers { get; set; }

    /// <summary>Copied from <see cref="Game.MinPlayers"/> at creation, for the same reason.</summary>
    public int MinPlayers { get; set; }

    /// <summary>
    /// Denormalised member count, maintained inside the same transaction as membership.
    /// <para>
    /// **It exists so capacity can be enforced by one atomic conditional UPDATE**, not to avoid a
    /// COUNT. Two clients racing for the last seat both run
    /// <c>UPDATE … SET CurrentPlayerCount = CurrentPlayerCount + 1 WHERE CurrentPlayerCount &lt; MaxPlayers</c>;
    /// exactly one affects a row. No SELECT takes part in the decision, so no isolation level above
    /// READ COMMITTED is needed and there is no lock ordering to get wrong.
    /// </para>
    /// </summary>
    public int CurrentPlayerCount { get; set; }

    /// <summary>
    /// The realtime contract version, owned by the platform and bumped whenever networked
    /// properties, RPC signatures, spawn order or the input struct change incompatibly.
    /// <para>
    /// **Not the app version.** A client on a different version is refused at join rather than
    /// allowed into a room where it would desync. Accepted values are server configuration, so a
    /// transitional window during a staged rollout is an ops change rather than a deploy.
    /// </para>
    /// </summary>
    public int ProtocolVersion { get; set; }

    /// <summary>
    /// The curriculum path the match plays, stored verbatim and **opaque to the backend** — it is
    /// echoed back on every response and nothing here validates it, because the client already did.
    /// Not a foreign key.
    /// </summary>
    public string? CurriculumPathJson { get; set; }

    /// <summary>
    /// The lesson out of that path, lifted into its own column.
    /// <para>
    /// Matchmaking filters on exactly one field of the curriculum path — the lesson — and a filter
    /// that has to parse JSON cannot use an index. So the blob above stays opaque and this column
    /// carries the one value the candidate query joins on. Null when the caller sent no path.
    /// </para>
    /// </summary>
    /// <summary>
    /// The lesson every player in this match will answer, or null while the roster is still forming.
    /// <para>
    /// <b>Null is a real and normal state now.</b> A subject-scoped match is created before anyone
    /// knows which lesson the players have in common, and the lesson is stamped once there are enough
    /// of them — see <see cref="EligibleLessons"/>. Sessions created with an explicit lesson (an
    /// older client, or a direct invite) carry it from the start and never move.
    /// </para>
    /// </summary>
    public Guid? LessonId { get; set; }

    /// <summary>
    /// The subject the players chose. Set when the match is subject-scoped, which is what makes
    /// matchmaking able to look for a lesson the players share rather than demanding they picked the
    /// same one.
    /// </summary>
    public Guid? SubjectId { get; set; }

    /// <summary>
    /// The content language this match is played in. Questions exist per language, so two children on
    /// different languages cannot share a lesson even when they share the unlock.
    /// </summary>
    public Guid? LangId { get; set; }

    /// <summary>The mode being played, when the client named one. Matchmaking never mixes two.</summary>
    public Guid? ModeId { get; set; }

    /// <summary>The event this match is an entry in, when it is one.</summary>
    public Guid? EventId { get; set; }

    /// <summary>
    /// The lessons every seated player can still play — the running intersection of what each of
    /// them has unlocked and has questions for.
    /// <para>
    /// Narrowed on every seat rather than recomputed at start: a lesson that leaves the set because
    /// the third player has not unlocked it must not come back if they leave again, or the match
    /// would start on a lesson somebody was shown as impossible.
    /// </para>
    /// </summary>
    public ICollection<MultiplayerSessionEligibleLesson> EligibleLessons { get; set; } =
        new List<MultiplayerSessionEligibleLesson>();

    public bool IsRanked { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Set when the session reaches <c>Running</c>.</summary>
    public DateTime? StartedAtUtc { get; set; }

    /// <summary>Set on the first terminal transition, and never moved by a later one.</summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// Advanced by the host's heartbeat, **always from the server clock**. A client-supplied time is
    /// never written here: a device with a skewed or edited clock could otherwise keep a dead
    /// session alive forever, or kill a live one.
    /// </summary>
    public DateTime LastHeartbeatAtUtc { get; set; }

    public SessionClosedReason? ClosedReason { get; set; }

    /// <summary>
    /// Optimistic concurrency for state transitions and host migration. Two members claiming a
    /// vacant host slot at the same instant both read the same version; exactly one write commits
    /// and the loser re-reads to find the winner rather than retrying blindly.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public ICollection<MultiplayerSessionPlayer> Players { get; set; } = new List<MultiplayerSessionPlayer>();
}
