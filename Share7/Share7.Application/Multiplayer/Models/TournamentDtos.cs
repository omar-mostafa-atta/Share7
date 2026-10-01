using Share7.Domain.Multiplayer;

namespace Share7.Application.Multiplayer.Models;

/// <summary>
/// A new tournament. <c>eventId</c> makes it a prize-bearing event tournament (operators only);
/// <c>cohortId</c> a classroom one, entered only by that class. Neither is an open tournament.
/// </summary>
public class CreateTournamentRequest
{
    public string Title { get; set; } = string.Empty;

    public Guid GameId { get; set; }

    /// <summary>A versus mode of the game with a win rule.</summary>
    public Guid ModeId { get; set; }

    public TournamentFormat Format { get; set; } = TournamentFormat.SingleElimination;

    /// <summary>Swiss only. Absent or 0: log₂ of the field.</summary>
    public int? SwissRounds { get; set; }

    public int? MaxEntrants { get; set; }

    /// <summary>How long each pairing has before a no-show is called.</summary>
    public int? MatchMinutes { get; set; }

    /// <summary>When it starts by itself. Absent: its organiser starts it.</summary>
    public DateTime? StartsAtUtc { get; set; }

    public Guid? EventId { get; set; }

    public Guid? CohortId { get; set; }

    /// <summary>A lesson every match plays, or a subject each pairing plays a shared lesson from, or neither.</summary>
    public CurriculumPathDto? CurriculumPath { get; set; }
}

/// <summary>Press play on your pairing. The first of the pair to do so hosts.</summary>
public class PlayTournamentMatchRequest : MultiplayerRequest
{
    /// <summary>The room to create, if you turn out to be the host. Ignored if the room is already open.</summary>
    public string TransportSessionName { get; set; } = string.Empty;

    public string? TransportRegion { get; set; }

    public int ProtocolVersion { get; set; }
}

/// <summary>An organiser settling a pairing by hand: a winner, or a fresh game.</summary>
public class DecideTournamentMatchRequest
{
    /// <summary>Who goes through. Ignored when <see cref="Replay"/> is set.</summary>
    public Guid? WinnerUserId { get; set; }

    /// <summary>Play it again in a fresh room, with a fresh deadline.</summary>
    public bool Replay { get; set; }

    /// <summary>Why — kept in the audit log, never shown to the players.</summary>
    public string Reason { get; set; } = string.Empty;
}

public class OrganiserReasonRequest
{
    public string? Reason { get; set; }
}

public class TournamentSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;

    public Guid GameId { get; set; }
    public Guid ModeId { get; set; }
    public string? ModeKey { get; set; }

    /// <summary><c>open</c>, <c>event</c> or <c>classroom</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    public Guid? EventId { get; set; }
    public Guid? CohortId { get; set; }

    public TournamentFormat Format { get; set; }
    public TournamentState State { get; set; }

    public int EntrantCount { get; set; }
    public int MaxEntrants { get; set; }
    public int CurrentRound { get; set; }

    /// <summary>Rounds it will take. Known once it starts; 0 before.</summary>
    public int RoundCount { get; set; }

    public int MatchMinutes { get; set; }

    public CurriculumPathDto? CurriculumPath { get; set; }

    public DateTime? StartsAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The caller's entry, if they have one.</summary>
    public TournamentEntryState? MyState { get; set; }
    public int? MyPlacement { get; set; }

    /// <summary>The caller organises it: may start, cancel and settle disputes.</summary>
    public bool CanManage { get; set; }
}

public class TournamentDto : TournamentSummaryDto
{
    public string? CancelReason { get; set; }

    /// <summary>Everyone entered, in standing order: placement once final, otherwise points then seed.</summary>
    public List<TournamentEntrantDto> Entrants { get; set; } = [];

    /// <summary>Every round paired so far, earliest first.</summary>
    public List<TournamentRoundDto> Rounds { get; set; } = [];

    /// <summary>The caller's pairing in the current round, if they have one still to play.</summary>
    public TournamentMatchDto? MyMatch { get; set; }

    public DateTime ServerTimeUtc { get; set; }
}

public class TournamentEntrantDto
{
    public Guid UserId { get; set; }

    /// <summary>The public handle, never a real name — even in a classroom tournament.</summary>
    public string? DisplayName { get; set; }

    public int Seed { get; set; }
    public TournamentEntryState State { get; set; }

    /// <summary>Swiss points: 1 a win or bye, ½ a draw.</summary>
    public double Points { get; set; }

    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int Byes { get; set; }
    public int? Placement { get; set; }
}

public class TournamentRoundDto
{
    public int Round { get; set; }
    public List<TournamentMatchDto> Matches { get; set; } = [];
}

public class TournamentMatchDto
{
    public Guid Id { get; set; }
    public int Round { get; set; }
    public int Position { get; set; }

    public Guid? PlayerAUserId { get; set; }
    public string? PlayerAName { get; set; }
    public Guid? PlayerBUserId { get; set; }
    public string? PlayerBName { get; set; }

    public TournamentMatchState State { get; set; }
    public int GameNumber { get; set; }

    /// <summary>The current game's room, once one of the pair has opened it.</summary>
    public Guid? SessionId { get; set; }

    public bool PlayerACheckedIn { get; set; }
    public bool PlayerBCheckedIn { get; set; }

    public DateTime? DeadlineAtUtc { get; set; }

    public Guid? WinnerUserId { get; set; }

    /// <summary>How it was decided: <c>played</c>, <c>draw</c>, <c>walkover</c>, <c>bye</c>, <c>seed</c>, <c>no_show</c>, <c>no_result</c>, <c>not_played</c>, <c>forfeit</c>, <c>organiser</c>, <c>empty</c>.</summary>
    public string? Outcome { get; set; }

    public bool Flagged { get; set; }
}

/// <summary>What pressing play answers: the room, and whether you bring it up.</summary>
public class TournamentPlayDto
{
    public TournamentMatchDto Match { get; set; } = new();

    public MultiplayerSessionDto Session { get; set; } = new();

    /// <summary>
    /// True: create the transport room named in the session and <c>start</c> it, as any host does.
    /// False: your opponent is bringing it up — join this session once it reads <c>Created</c>.
    /// </summary>
    public bool YouHost { get; set; }
}
