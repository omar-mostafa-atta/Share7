using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Models;

namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// Tournaments over reserved sessions (MultiplayerPlatform.md §9.3): a bracket or a Swiss, each
/// pairing a private room reserved for its two players and decided by the ordinary match result.
/// <para>
/// <b>Organisers</b> are operators (<c>asAdmin</c>) for any tournament, and a class's teachers for
/// that class's own tournaments — the relationship is a cohort membership, never a role claim.
/// </para>
/// <para>
/// <b>Advancing is lazy and idempotent.</b> Reading a tournament settles what is due — decided
/// results, passed deadlines, a finished round — and the sweeper does the same for the rest, so no
/// single request or server has to be the one that moves it on.
/// </para>
/// </summary>
public interface ITournamentService
{
    /// <summary>Tournaments the caller may see and enter, newest first, plus every one they are in.</summary>
    Task<IReadOnlyList<TournamentSummaryDto>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The bracket or standings. <c>TOURNAMENT_NOT_FOUND</c> for one the caller cannot see; an
    /// operator (<paramref name="asAdmin"/>) sees every one.
    /// </summary>
    Task<ServiceResult<TournamentDto>> GetAsync(Guid userId, Guid tournamentId, bool asAdmin = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enters the caller. Entering again returns the same tournament. Refusals:
    /// <c>TOURNAMENT_REGISTRATION_CLOSED</c>, <c>TOURNAMENT_FULL</c>, <c>TOURNAMENT_NOT_ELIGIBLE</c>
    /// (not in the class), <c>TOURNAMENT_LESSON_LOCKED</c>, <c>PC_NO_SHARED_LESSON</c>, and the mode's
    /// or event's own <c>PC_*</c>.
    /// </summary>
    Task<ServiceResult<TournamentDto>> RegisterAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>Leaves. Before the start the place is freed; after it, a pairing still to play goes to the opponent.</summary>
    Task<ServiceResult<TournamentDto>> WithdrawAsync(Guid userId, Guid tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Presses play on the caller's pairing: the first of the pair opens a room reserved for both and
    /// hosts it; the second is handed the same room to join. Idempotent on the request id.
    /// Refusals: <c>TOURNAMENT_MATCH_NOT_FOUND</c>, <c>TOURNAMENT_MATCH_CLOSED</c>,
    /// <c>ALREADY_IN_SESSION</c>, <c>PROTOCOL_VERSION_MISMATCH</c>, and the mode's own <c>PC_*</c>.
    /// </summary>
    Task<ServiceResult<TournamentPlayDto>> PlayAsync(
        Guid userId, Guid tournamentId, Guid matchId, PlayTournamentMatchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Creates one. A teacher may create one for a class they teach; anything else needs <paramref name="asAdmin"/>.</summary>
    Task<ServiceResult<TournamentDto>> CreateAsync(
        Guid userId, CreateTournamentRequest request, bool asAdmin, CancellationToken cancellationToken = default);

    /// <summary>Starts it now. Fewer than two entrants cancels it instead.</summary>
    Task<ServiceResult<TournamentDto>> StartAsync(Guid userId, Guid tournamentId, bool asAdmin, CancellationToken cancellationToken = default);

    /// <summary>Calls it off. Nothing is placed and nothing is paid.</summary>
    Task<ServiceResult<TournamentDto>> CancelAsync(
        Guid userId, Guid tournamentId, string? reason, bool asAdmin, CancellationToken cancellationToken = default);

    /// <summary>Settles a pairing of the current round by hand — a winner, or a replay. Audited.</summary>
    Task<ServiceResult<TournamentDto>> DecideMatchAsync(
        Guid userId, Guid tournamentId, Guid matchId, DecideTournamentMatchRequest request, bool asAdmin, CancellationToken cancellationToken = default);

    /// <summary>Removes an entrant: never placed, never paid; a pairing still to play goes to the opponent. Audited.</summary>
    Task<ServiceResult<TournamentDto>> DisqualifyAsync(
        Guid userId, Guid tournamentId, Guid entrantUserId, string reason, bool asAdmin, CancellationToken cancellationToken = default);

    /// <summary>Every tournament, for the console, newest first.</summary>
    Task<IReadOnlyList<TournamentSummaryDto>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// One sweeper pass: start what is due, advance what is running, and pay any event prize table
    /// whose payout has not landed. Returns the number of tournaments moved.
    /// </summary>
    Task<int> AdvanceDueAsync(CancellationToken cancellationToken = default);
}
