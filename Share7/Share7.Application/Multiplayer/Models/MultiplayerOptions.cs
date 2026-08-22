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
