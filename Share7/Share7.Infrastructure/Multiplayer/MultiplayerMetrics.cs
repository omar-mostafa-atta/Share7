using System.Diagnostics.Metrics;
using Share7.Application.Common.Models;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Operational signals for the session registry, on the in-box <see cref="Meter"/> API.
/// <para>
/// **Operations, not product analytics.** "How many children matchmade into Algebra" is a question for
/// the telemetry pipeline, which the client feeds (<c>matchmaking_started</c>, <c>match_finished</c>).
/// These answer the 3 AM questions instead — is matchmaking creating instead of joining, are joins
/// being refused as full, is the sweeper abandoning sessions, how long does a matchmake take — and they
/// cost nothing until a listener attaches: <c>dotnet-counters monitor --counters Share7.Multiplayer</c>
/// reads them from a live process, and an OpenTelemetry exporter can be added later without touching a
/// single call site.
/// </para>
/// <para>
/// **Tags are low-cardinality on purpose.** Outcomes are error codes and a handful of tokens; no
/// session, user or game id is ever a tag, because a tag per id is a time series per id.
/// </para>
/// </summary>
internal static class MultiplayerMetrics
{
    public const string MeterName = "Share7.Multiplayer";

    private static readonly Meter Meter = new(MeterName, "1.0");

    /// <summary>Sessions created, by visibility and whether matchmaking created them.</summary>
    public static readonly Counter<long> SessionsCreated =
        Meter.CreateCounter<long>("share7.multiplayer.sessions.created", description: "Sessions created.");

    /// <summary>Seat attempts through join or matchmaking, by outcome code.</summary>
    public static readonly Counter<long> Seats =
        Meter.CreateCounter<long>("share7.multiplayer.seats", description: "Seat attempts by outcome.");

    /// <summary>Lifecycle moves — confirm, start, leave, close, host transfer — by operation and outcome.</summary>
    public static readonly Counter<long> Transitions =
        Meter.CreateCounter<long>("share7.multiplayer.transitions", description: "Session lifecycle operations by outcome.");

    /// <summary>
    /// Sessions that reached a terminal state, by <c>SessionClosedReason</c>. The ratio of
    /// <c>Abandoned</c> and <c>CreationFailed</c> to the rest is the single best health signal this
    /// domain has: it rises when hosts crash, when the transport is down, or when a client build
    /// stops confirming its rooms.
    /// </summary>
    public static readonly Counter<long> SessionsEnded =
        Meter.CreateCounter<long>("share7.multiplayer.sessions.ended", description: "Sessions ended, by reason.");

    /// <summary>
    /// Matches decided, by state (<c>Decided</c>/<c>Unranked</c>) and by how (<c>all_reported</c> or
    /// <c>deadline</c>). A rising share of deadline verdicts means players are dropping before their
    /// results reach the server.
    /// </summary>
    public static readonly Counter<long> MatchResults =
        Meter.CreateCounter<long>("share7.multiplayer.match_results", description: "Matches decided, by state and trigger.");

    /// <summary>Tournament entries taken.</summary>
    public static readonly Counter<long> TournamentEntries =
        Meter.CreateCounter<long>("share7.multiplayer.tournament.entries", description: "Tournament entries taken.");

    /// <summary>Tournaments started and completed, by format.</summary>
    public static readonly Counter<long> TournamentsStarted =
        Meter.CreateCounter<long>("share7.multiplayer.tournament.started", description: "Tournaments started, by format.");

    public static readonly Counter<long> TournamentsCompleted =
        Meter.CreateCounter<long>("share7.multiplayer.tournament.completed", description: "Tournaments completed, by format.");

    /// <summary>Tournament pairings settled, by how — played, walkover, no_show, seed. A climbing no-show share is a deadline set too short.</summary>
    public static readonly Counter<long> TournamentMatches =
        Meter.CreateCounter<long>("share7.multiplayer.tournament.matches", description: "Tournament pairings settled, by outcome.");

    /// <summary>Host heartbeats, by outcome.</summary>
    public static readonly Counter<long> Heartbeats =
        Meter.CreateCounter<long>("share7.multiplayer.heartbeats", description: "Host heartbeats by outcome.");

    /// <summary>Matchmaking calls, by outcome (<c>Joined</c>, <c>Created</c>, <c>NoMatch</c>, or a refusal code).</summary>
    public static readonly Counter<long> Matchmakes =
        Meter.CreateCounter<long>("share7.multiplayer.matchmaking.requests", description: "Matchmaking requests by outcome.");

    /// <summary>Wall time of one matchmaking call, in milliseconds.</summary>
    public static readonly Histogram<double> MatchmakeDuration =
        Meter.CreateHistogram<double>("share7.multiplayer.matchmaking.duration", unit: "ms", description: "Matchmaking request duration.");

    /// <summary>How many candidate sessions one matchmaking call tried before it joined or created.</summary>
    public static readonly Histogram<int> CandidatesTried =
        Meter.CreateHistogram<int>("share7.multiplayer.matchmaking.candidates_tried", description: "Candidates tried per matchmaking request.");

    /// <summary>Rows the sweeper changed, by rule.</summary>
    public static readonly Counter<long> Swept =
        Meter.CreateCounter<long>("share7.multiplayer.sweep.rows", description: "Rows changed by the sweeper, by rule.");

    /// <summary>Requests answered from the idempotency log instead of being run again, by operation.</summary>
    public static readonly Counter<long> Replays =
        Meter.CreateCounter<long>("share7.multiplayer.replays", description: "Requests answered from the idempotency log.");

    /// <summary>
    /// Transport tickets issued, and Photon's authentication calls by outcome. A burst of
    /// <c>bad_key</c> is someone other than Photon calling the endpoint; a steady share of
    /// <c>invalid_ticket</c> is clients connecting with stale tickets.
    /// </summary>
    public static readonly Counter<long> TransportAuth =
        Meter.CreateCounter<long>("share7.multiplayer.transport_auth", description: "Transport tickets and Photon authentication calls, by outcome.");

    /// <summary>
    /// Matchmaking tickets by outcome (<c>queued</c>, <c>matched</c>, <c>placed</c>, <c>expired</c>) and ranked or not. A
    /// high expired share means a pool too thin for its band or fill settings.
    /// </summary>
    public static readonly Counter<long> Tickets =
        Meter.CreateCounter<long>("share7.multiplayer.tickets", description: "Matchmaking tickets by outcome.");

    /// <summary>The outcome tag for a service result: <c>ok</c>, or the refusal's code.</summary>
    public static string Outcome(ServiceResult result) =>
        result.Succeeded ? "ok" : result.Error?.Code ?? result.ErrorKind.ToString();

    public static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);
}
