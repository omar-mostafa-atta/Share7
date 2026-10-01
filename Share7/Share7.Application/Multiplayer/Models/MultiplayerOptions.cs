namespace Share7.Application.Multiplayer.Models;

/// <summary>
/// Configuration for multiplayer sessions, bound from the <c>Multiplayer</c> section.
/// <para>
/// The client mirrors several of these on its own <c>NetworkingConfig</c>. Where a value is also
/// returned in a response — the heartbeat interval, most importantly — **the server's value wins**;
/// the client's copy is a starting guess for the first call, not an authority.
/// </para>
/// </summary>
public class MultiplayerOptions
{
    public const string SectionName = "Multiplayer";

    /// <summary>
    /// How often the host is expected to check in. Cheap at this scale: four writes a minute per
    /// live session, and only from the host rather than from every member.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Silence beyond this and the sweeper abandons the session — four missed heartbeats at the
    /// default interval. Sized to survive a lift, a tunnel, and a Wi-Fi to cellular handover, all of
    /// which are ordinary on a phone and none of which should kill a match.
    /// </summary>
    public int SessionTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// A session stuck in <c>Creating</c> for this long never had a transport room, and is failed
    /// rather than left to occupy its room name.
    /// </summary>
    public int CreatingTimeoutSeconds { get; set; } = 30;

    /// <summary>How long a missing member keeps their slot before the sweeper releases it.</summary>
    public int PlayerDisconnectGraceSeconds { get; set; } = 45;

    /// <summary>
    /// How long a player the backend seated may take to show up on the host's realtime roster before
    /// they are treated as missing.
    /// <para>
    /// Separate from <see cref="PlayerDisconnectGraceSeconds"/> because the two measure different
    /// things. A dropped player was already in the room; a newly seated one still has to load the
    /// scene and connect to the transport on whatever phone they have, which is the slow half of
    /// joining. Without this bound at all, a player whose app died between the join response and the
    /// room held their seat until the session ended — in a two-seat game, a full lobby with an
    /// opponent who was never coming.
    /// </para>
    /// </summary>
    public int JoinedConnectGraceSeconds { get; set; } = 60;

    /// <summary>
    /// Where the name on each seat comes from. Defaults to <see cref="RosterNameSource.Handle"/>.
    /// <para>
    /// **The default is the child-safety ruling, not a preference.** Public matchmaking seats
    /// strangers together, and <c>StudentProfile.FullName</c> is a child's real name — the leaderboards
    /// refuse to show it for exactly that reason. <see cref="RosterNameSource.ProfileName"/> exists
    /// only so the change is reversible by configuration if the product owner rules otherwise; it
    /// should not be turned on without a consent decision behind it.
    /// </para>
    /// </summary>
    public RosterNameSource RosterNames { get; set; } = RosterNameSource.Handle;

    /// <summary>
    /// Whether a private session may only be entered with its join code. **Off by default, and it
    /// should be switched on once the client that joins by code has shipped.**
    /// <para>
    /// Until then a private session is private only from matchmaking: anyone who learns its id — and
    /// the id travels in the transport room's properties — can join it by id. The shipped build may
    /// join friends' rooms exactly that way, so closing it is a rollout decision rather than a fix
    /// that can go out on its own. Someone who has held a seat in the session keeps the by-id route
    /// either way, so a player reconnecting after a drop is never locked out of their own match.
    /// </para>
    /// </summary>
    public bool RequireJoinCodeForPrivateSessions { get; set; }

    /// <summary>
    /// How long after a match ends its result waits for players who have not reported yet, before
    /// they forfeit.
    /// <para>
    /// A result is decided at once if everyone has reported; this only covers the stragglers — a
    /// phone that lost signal on the last question and syncs a minute later. Long enough for an
    /// ordinary reconnect, short enough that the players who did report are not left staring at a
    /// "waiting for results" screen. A result that arrives later still pays; it just does not move a
    /// placement somebody has already been shown.
    /// </para>
    /// </summary>
    public int MatchResultGraceSeconds { get; set; } = 120;

    /// <summary>
    /// How long a transport ticket — what the client hands Photon to prove who it is — stays valid.
    /// <para>
    /// Photon asks the backend about it within a second or two of the client connecting, so two
    /// minutes is generous; it is kept short because the ticket passes through Photon's servers, and
    /// whoever holds one could connect as that player until it expires. A client fetches a fresh one
    /// before each connect.
    /// </para>
    /// </summary>
    public int TransportTicketSeconds { get; set; } = 120;

    /// <summary>
    /// A shared secret Photon adds to every authentication call (a key/value pair in the Photon
    /// dashboard's custom-authentication settings, sent as <c>?key=</c>). When set, a call without
    /// it is refused before any other work. **Supply it from the environment or a secret store, never
    /// from a committed file.** Empty means not required — the ticket's signature still is.
    /// </summary>
    public string? PhotonAuthKey { get; set; }

    /// <summary>How long an invite into a room waits for an answer. A lobby rarely waits longer.</summary>
    public int InvitationMinutes { get; set; } = 10;

    /// <summary>
    /// How long feed events are kept after they occur. A client whose cursor is older is told to
    /// re-read state (<c>EVENTS_CURSOR_EXPIRED</c>) rather than miss events silently.
    /// </summary>
    public int EventRetentionDays { get; set; } = 7;

    /// <summary>The longest a feed read waits for an event before answering empty.</summary>
    public int EventMaxWaitSeconds { get; set; } = 25;

    /// <summary>
    /// How often a waiting feed read checks the database even without an in-process wake-up — the
    /// path for an event committed by another server instance. On one instance, events wake readers
    /// immediately and this is only a backstop.
    /// </summary>
    public double EventFallbackPollSeconds { get; set; } = 5;

    /// <summary>How recently a client must have polled its feed to count as online.</summary>
    public int PresenceOnlineSeconds { get; set; } = 90;

    /// <summary>At most one presence write per player per this many seconds.</summary>
    public int PresenceWriteSeconds { get; set; } = 30;

    /// <summary>The most players a party holds. Four: a party is friends playing together, not a lobby.</summary>
    public int PartyMaxSize { get; set; } = 4;

    /// <summary>
    /// The largest field a tournament may take. 256 is a bracket of eight rounds — about as long as
    /// anyone waits on a result — and a Swiss pairing still computed in milliseconds.
    /// </summary>
    public int TournamentMaxEntrants { get; set; } = 256;

    /// <summary>
    /// How long a tournament pairing has to be played, from the moment it is ready, unless its
    /// organiser sets another. Fifteen minutes suits a lesson period; a week-long event sets a day.
    /// </summary>
    public int TournamentMatchMinutes { get; set; } = 15;

    /// <summary>Rated matches a season starts with before a tier is shown.</summary>
    public int RankedPlacementMatches { get; set; } = 5;

    /// <summary>
    /// Anti-boosting: after this many rated matches against the same opponent in a day, further ones
    /// between them stop moving either rating. Two friends queueing together all evening cannot farm
    /// each other.
    /// </summary>
    public int RankedRepeatOpponentLimitPerDay { get; set; } = 5;

    /// <summary>
    /// Uncertainty added to a rating at its first match of a new season, so the new season's placements
    /// mean something without throwing away what is known.
    /// </summary>
    public double RankedSeasonSigmaBump { get; set; } = 25.0 / 6.0;

    /// <summary>How long a matchmaking ticket searches before it expires and the player is offered something else.</summary>
    public int TicketSeconds { get; set; } = 180;

    /// <summary>How often the matchmaking worker forms matches from waiting tickets.</summary>
    public double TicketIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// How long the longest-waiting ranked ticket waits for a full match before settling for a smaller one
    /// (never fewer than two players).
    /// </summary>
    public int RankedFillAfterSeconds { get; set; } = 15;

    /// <summary>How long a casual ticket waits for company before it opens a public room others can join.</summary>
    public int CasualFillAfterSeconds { get; set; } = 10;

    /// <summary>
    /// Ranked matching: how far apart two ratings (mean skill) may be, starting at
    /// <see cref="RankedBandBase"/> and widening by <see cref="RankedBandGrowthPerSecond"/> for every
    /// second the longer-waiting of the two has queued, up to <see cref="RankedBandMax"/>. Nobody waits
    /// forever for a perfect opponent; nobody meets a mismatch in the first seconds.
    /// </summary>
    public double RankedBandBase { get; set; } = 5.0;

    public double RankedBandGrowthPerSecond { get; set; } = 0.5;

    public double RankedBandMax { get; set; } = 30.0;

    /// <summary>How long the current host must be unseen before another member may claim authority.</summary>
    public int HostClaimGraceSeconds { get; set; } = 30;

    /// <summary>Sessions considered per matchmaking attempt before giving up and creating one.</summary>
    public int MatchmakingCandidateLimit { get; set; } = 10;

    /// <summary>
    /// How long a completed operation stays replayable. Far longer than any plausible client retry
    /// budget, which is the point — the window should expire long after the client has given up.
    /// </summary>
    public int RequestLogRetentionHours { get; set; } = 24;

    /// <summary>How often the sweeper runs.</summary>
    public int SweepIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Realtime contract versions the server will currently seat, as configured.
    /// <para>
    /// **A list rather than a single number, deliberately.** During a staged rollout two client
    /// builds are live at once, and both have to be able to play — with one accepted version the
    /// older build is locked out the moment the newer one ships. Widening the window for a rollout
    /// is then an ops change to this array rather than a deploy.
    /// </para>
    /// <para>
    /// **Defaults to empty, not to <c>[1]</c>, and that is not a detail.** The configuration binder
    /// *appends* to a collection that already has items rather than replacing it, so a property
    /// initialised to <c>[1]</c> and configured as <c>[2]</c> binds to <c>[1, 2]</c> — version 1
    /// would survive every attempt to retire it, and closing a rollout window is exactly as much an
    /// ops action as opening one. The fallback lives in <see cref="EffectiveProtocolVersions"/>
    /// instead, where configuration can actually override it.
    /// </para>
    /// </summary>
    public List<int> AcceptedProtocolVersions { get; set; } = [];

    /// <summary>
    /// What is actually enforced: the configured versions, or <c>[1]</c> when none are set.
    /// <para>
    /// An empty configured list falls back rather than accepting nothing. A server that seats no
    /// protocol version at all refuses every match, which is a total outage dressed up as a config
    /// value — so it is treated as "unconfigured" instead. To stop accepting a version, name the
    /// ones you still want.
    /// </para>
    /// </summary>
    public IReadOnlyList<int> EffectiveProtocolVersions =>
        AcceptedProtocolVersions.Count > 0 ? AcceptedProtocolVersions : DefaultProtocolVersions;

    private static readonly int[] DefaultProtocolVersions = [1];
}

/// <summary>Where a roster seat's name comes from. See <see cref="MultiplayerOptions.RosterNames"/>.</summary>
public enum RosterNameSource
{
    /// <summary>The platform-generated public handle — the one the leaderboards show. Carries no personal data.</summary>
    Handle = 0,

    /// <summary>The student's profile name. A real name; see the warning on <see cref="MultiplayerOptions.RosterNames"/>.</summary>
    ProfileName = 1
}
