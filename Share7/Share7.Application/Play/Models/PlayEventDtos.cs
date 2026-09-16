using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Play.Models;

/// <summary>
/// One competition, as an entrant sees it: when it runs, what it is played in, what winning is worth
/// and whether they may enter.
/// <para>
/// <b>The window and the state come from the bound leaderboard cycle</b>, never from a column on the
/// event and never from device time. <see cref="PlayEventsResponse.ServerTimeUtc"/> is beside them
/// for exactly that reason.
/// </para>
/// </summary>
public class PlayEventDto
{
    public Guid EventId { get; init; }
    public string EventKey { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>How to win, in the entrant's language. Authored per event, not a client string key.</summary>
    public string Rules { get; init; } = string.Empty;

    public string GameKey { get; init; } = string.Empty;
    public string ModeKey { get; init; } = string.Empty;

    /// <summary>The world every entry runs in, or null to leave the mode's own selection alone.</summary>
    public string? WorldKey { get; init; }

    /// <summary>True when entrants may play <see cref="WorldKey"/> without owning it, for the duration.</summary>
    public bool GrantsWorldForDuration { get; init; }

    /// <summary>The ladder, for a client that wants to read the full standings through the leaderboard API.</summary>
    public string BoardKey { get; init; } = string.Empty;
    public Guid BoardId { get; init; }
    public Guid CycleId { get; init; }

    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }

    /// <summary><c>SCHEDULED</c>, <c>OPEN</c>, <c>CLOSED</c> or <c>SETTLED</c> — the cycle's own state.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Called off by an operator. Shown as cancelled rather than hidden, so entrants are told.</summary>
    public bool IsCancelled { get; init; }

    public string? BannerAddress { get; init; }
    public string? AccentColor { get; init; }
    public int SortOrder { get; init; }

    /// <summary><c>All</c> or <c>Grade</c> — which slice of players the prize table ranks within.</summary>
    public string PrizeCohort { get; init; } = string.Empty;

    /// <summary>How many players hold a rank in this event so far.</summary>
    public int Participants { get; init; }

    public PlayEventRulesDto EntryRules { get; init; } = new();

    public IReadOnlyList<EventPrizeTierDto> Prizes { get; init; } = [];

    /// <summary>Whether this account may enter right now, and why not when it may not.</summary>
    public bool Eligible { get; init; }

    /// <summary>A <c>PC_*</c> code when <see cref="Eligible"/> is false, so the client renders the right sentence.</summary>
    public string? IneligibleCode { get; init; }

    public int EntriesToday { get; init; }
    public int EntriesTotal { get; init; }

    /// <summary>The caller's own rank in the prize cohort, or null when they have not entered.</summary>
    public int? MyRank { get; init; }

    public long? MyValue { get; init; }
}

/// <summary>The conditions an entry has to meet. All optional; zero and null both mean "no limit".</summary>
public class PlayEventRulesDto
{
    public int? MaxEntriesPerDay { get; init; }
    public int? MaxEntriesTotal { get; init; }
    public int MinGradeOrder { get; init; }
    public int MaxGradeOrder { get; init; }
    public int MinLevel { get; init; }

    /// <summary>The product an entrant must own, when the event is members-only. Null for an open event.</summary>
    public string? RequiresSku { get; init; }
}

/// <summary>One band of the prize table, as it is advertised.</summary>
public class EventPrizeTierDto
{
    public Guid TierId { get; init; }
    public int FromRank { get; init; }
    public int ToRank { get; init; }

    /// <summary><c>IN_GAME</c> or <c>REAL_WORLD</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>How many of this prize exist, or null for unlimited.</summary>
    public int? Quantity { get; init; }

    public int SortOrder { get; init; }
}

public class PlayEventsResponse
{
    public DateTime ServerTimeUtc { get; init; }

    public IReadOnlyList<PlayEventDto> Events { get; init; } = [];
}

/// <summary>
/// Something this account won, and what has happened to it since.
/// <para>
/// A real-world prize carries its claim's state and deadline. It carries <b>no</b> address, phone
/// number or payment detail, here or anywhere else — fulfilment happens through a channel the
/// operator already has a lawful basis for, and a prize is not a reason to start collecting a
/// child's personal data.
/// </para>
/// </summary>
public class EventAwardDto
{
    public Guid AwardId { get; init; }
    public Guid EventId { get; init; }
    public string EventName { get; init; } = string.Empty;

    public string PrizeTitle { get; init; } = string.Empty;
    public string PrizeDescription { get; init; } = string.Empty;

    /// <summary><c>IN_GAME</c> or <c>REAL_WORLD</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    public int FinalRank { get; init; }
    public long Value { get; init; }

    /// <summary><c>GRANTED</c>, <c>AWAITING_CLAIM</c>, <c>FULFILLED</c>, <c>FORFEITED</c> or <c>VOID</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The claim's state for a real-world prize; null for an in-game one.</summary>
    public string? ClaimState { get; init; }

    /// <summary>When an unanswered claim lapses. Shown so a winner knows there is a deadline at all.</summary>
    public DateTime? ClaimExpiresAtUtc { get; init; }

    public DateTime AwardedAtUtc { get; init; }

    /// <summary>Null until the winner has been shown it. What the "you won" surface keys on.</summary>
    public DateTime? SeenAtUtc { get; init; }
}

// ---- authoring ------------------------------------------------------------------------------

/// <summary>An event in the authoring shape: every language, the window, the rules and the prize table.</summary>
public class PlayEventAdminDto
{
    public Guid EventId { get; init; }
    public string EventKey { get; init; } = string.Empty;

    public Guid GameId { get; init; }
    public string GameKey { get; init; } = string.Empty;

    public Guid ModeId { get; init; }
    public string ModeKey { get; init; } = string.Empty;

    public string? WorldKey { get; init; }
    public bool GrantsWorldForDuration { get; init; }

    public Guid BoardId { get; init; }
    public string BoardKey { get; init; } = string.Empty;
    public Guid CycleId { get; init; }

    /// <summary>The metric the ladder ranks. Fixed after creation — the board is already collecting it.</summary>
    public string Metric { get; init; } = string.Empty;

    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public string State { get; init; } = string.Empty;

    public string PrizeCohort { get; init; } = string.Empty;

    public int? MaxEntriesPerDay { get; init; }
    public int? MaxEntriesTotal { get; init; }
    public int MinGradeOrder { get; init; }
    public int MaxGradeOrder { get; init; }
    public int MinLevel { get; init; }
    public Guid? EntryProductId { get; init; }
    public int ClaimWindowDays { get; init; }

    public Guid? EconomyProfileId { get; init; }
    public string EconomyProfileKey { get; init; } = string.Empty;

    public string? BannerAddress { get; init; }
    public string? AccentColor { get; init; }
    public int SortOrder { get; init; }

    public bool IsActive { get; init; }
    public DateTime? CancelledAtUtc { get; init; }
    public string? CancelReason { get; init; }

    public int Participants { get; init; }
    public int AwardsIssued { get; init; }

    public IReadOnlyList<PlayEventTranslationRequest> Translations { get; init; } = [];
    public IReadOnlyList<EventPrizeTierAdminDto> PrizeTiers { get; init; } = [];
}

public class EventPrizeTierAdminDto
{
    public Guid TierId { get; init; }
    public int FromRank { get; init; }
    public int ToRank { get; init; }
    public string Kind { get; init; } = string.Empty;
    public int? Quantity { get; init; }
    public int SortOrder { get; init; }

    public long? DeclaredValueMinor { get; init; }
    public string? ValueCurrencyCode { get; init; }

    /// <summary>What an in-game tier pays. Empty for a real-world tier.</summary>
    public IReadOnlyList<EventPrizeGrantRequest> Grants { get; init; } = [];

    public IReadOnlyList<EventPrizeTierTranslationRequest> Translations { get; init; } = [];
}

/// <summary>
/// Creates an event <b>and its ladder</b> in one call.
/// <para>
/// The board and its single cycle are created here, atomically, because an event without a ladder
/// has nowhere to rank and a ladder without an event has nobody to pay. The window is authored once,
/// onto the cycle, and read back through it — this request is the only place both halves are named
/// together.
/// </para>
/// </summary>
public class SavePlayEventRequest
{
    [Required, MaxLength(96)]
    public string EventKey { get; set; } = string.Empty;

    [Required]
    public Guid GameId { get; set; }

    [Required]
    public Guid ModeId { get; set; }

    [MaxLength(128)]
    public string? WorldKey { get; set; }

    public bool GrantsWorldForDuration { get; set; } = true;

    /// <summary>
    /// What the ladder ranks — a <c>LeaderboardMetrics</c> token. Fixed once the event exists: the
    /// board is already collecting results under it.
    /// </summary>
    [Required, MaxLength(48)]
    public string Metric { get; set; } = string.Empty;

    /// <summary>Best result wins, or the total of every entry. <c>best</c> or <c>sum</c>.</summary>
    [MaxLength(8)]
    public string Aggregation { get; set; } = "best";

    [Required]
    public DateTime StartsAtUtc { get; set; }

    [Required]
    public DateTime EndsAtUtc { get; set; }

    /// <summary><c>all</c> ranks everybody together; <c>grade</c> ranks each school year separately.</summary>
    [MaxLength(16)]
    public string PrizeCohort { get; set; } = "all";

    public int? MaxEntriesPerDay { get; set; }
    public int? MaxEntriesTotal { get; set; }

    [Range(0, 100)]
    public int MinGradeOrder { get; set; }

    [Range(0, 100)]
    public int MaxGradeOrder { get; set; }

    [Range(0, 1000)]
    public int MinLevel { get; set; }

    /// <summary>Members-only entry. Refused when any prize tier is real-world.</summary>
    public Guid? EntryProductId { get; set; }

    [Range(1, 365)]
    public int ClaimWindowDays { get; set; } = 30;

    public Guid? EconomyProfileId { get; set; }

    [MaxLength(256)]
    public string? BannerAddress { get; set; }

    [MaxLength(9)]
    public string? AccentColor { get; set; }

    [Range(0, 10_000)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    [Required, MinLength(1, ErrorMessage = "A name is required for every configured language.")]
    public List<PlayEventTranslationRequest> Translations { get; set; } = [];

    public List<SaveEventPrizeTierRequest> PrizeTiers { get; set; } = [];
}

public class PlayEventTranslationRequest
{
    [Required]
    public Guid LangId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Rules { get; set; } = string.Empty;
}

public class SaveEventPrizeTierRequest
{
    /// <summary>Null creates a tier; an existing id edits one.</summary>
    public Guid? TierId { get; set; }

    [Range(1, 1_000_000)]
    public int FromRank { get; set; } = 1;

    [Range(1, 1_000_000)]
    public int ToRank { get; set; } = 1;

    /// <summary><c>in_game</c> or <c>real_world</c>.</summary>
    [Required, MaxLength(16)]
    public string Kind { get; set; } = "in_game";

    /// <summary>What an in-game tier pays: currencies, products, or both. Refused for a real-world tier.</summary>
    public List<EventPrizeGrantRequest> Grants { get; set; } = [];

    /// <summary>What a real-world prize is worth, in minor units. Operator reporting only.</summary>
    public long? DeclaredValueMinor { get; set; }

    [MaxLength(3)]
    public string? ValueCurrencyCode { get; set; }

    public int? Quantity { get; set; }

    [Range(0, 10_000)]
    public int SortOrder { get; set; }

    [Required, MinLength(1, ErrorMessage = "A prize title is required for every configured language.")]
    public List<EventPrizeTierTranslationRequest> Translations { get; set; } = [];
}

/// <summary>One thing an in-game tier hands over: an amount of a currency, or a product.</summary>
public class EventPrizeGrantRequest
{
    /// <summary>The currency key, e.g. <c>coins</c>. Null when this grant is a product.</summary>
    [MaxLength(64)]
    public string? Currency { get; set; }

    [Range(0, 1_000_000_000)]
    public long Amount { get; set; }

    /// <summary>A product to grant — a world, a cosmetic, a badge. Null when this grant is currency.</summary>
    public Guid? ProductId { get; set; }
}

public class EventPrizeTierTranslationRequest
{
    [Required]
    public Guid LangId { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;
}

/// <summary>Calling an event off, with the reason recorded for the audit trail.</summary>
public class CancelPlayEventRequest
{
    [MaxLength(512)]
    public string? Reason { get; set; }
}

/// <summary>A prize claim as an operator works it.</summary>
public class PrizeClaimAdminDto
{
    public Guid ClaimId { get; init; }
    public Guid AwardId { get; init; }
    public Guid EventId { get; init; }
    public string EventKey { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;

    public Guid UserId { get; init; }

    /// <summary>The player's public handle — never their real name, which this system does not hold.</summary>
    public string DisplayName { get; init; } = string.Empty;

    public string PrizeTitle { get; init; } = string.Empty;
    public long? DeclaredValueMinor { get; init; }
    public string? ValueCurrencyCode { get; init; }

    public int FinalRank { get; init; }
    public string State { get; init; } = string.Empty;

    public DateTime ExpiresAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
    public DateTime? FulfilledAtUtc { get; init; }
    public string? ReviewNote { get; init; }
    public Guid? ReviewedByUserId { get; init; }
}

/// <summary>Moving a claim along. The transitions are fixed; the note is for whoever reads it next.</summary>
public class UpdatePrizeClaimRequest
{
    /// <summary><c>awaiting_guardian</c>, <c>fulfilled</c>, <c>forfeited</c> or <c>rejected</c>.</summary>
    [Required, MaxLength(32)]
    public string State { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Note { get; set; }
}
