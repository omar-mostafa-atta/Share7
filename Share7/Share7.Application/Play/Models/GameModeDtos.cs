using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Play.Models;

/// <summary>
/// A mode as the Unity client consumes it. Field names mirror <c>GameModeDefinition</c> so the
/// client deserializes straight into the model it already has.
/// <para>
/// <b>Every field here overrides the client's authored copy</b>, exactly as <c>GameDto</c> overrides
/// <c>MiniGameDefinitionSO</c>. The client's asset is what it runs on when this cannot be reached;
/// it is never what it prefers.
/// </para>
/// </summary>
public class GameModeDto
{
    public Guid ModeId { get; init; }

    /// <summary>The join to the Unity catalogue: equals <c>GameModeDefinition.modeId</c>.</summary>
    public string ModeKey { get; init; } = string.Empty;

    /// <summary>Resolved into the caller's content language. Empty when the mode has no name in it.</summary>
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary><c>["solo","versus"]</c>. The client parses these into its own topology set.</summary>
    public IReadOnlyList<string> Topologies { get; init; } = [];

    public int MinPlayers { get; init; }
    public int MaxPlayers { get; init; }

    /// <summary>
    /// False only in a <c>includeScheduled</c> read. The ordinary read omits everything that is not
    /// currently offered, so a client never has to decide whether to show something.
    /// </summary>
    public bool IsOffered { get; init; }

    public DateTime? AvailableFromUtc { get; init; }
    public DateTime? AvailableToUtc { get; init; }

    public bool RequiresEntitlement { get; init; }

    /// <summary>The product key the entitlement is granted under, or null. The client already holds its entitlements.</summary>
    public string? EntitlementSku { get; init; }

    public int MinGradeOrder { get; init; }

    public bool CountsTowardMastery { get; init; }
    public bool SettlesEconomy { get; init; }
    public bool CountsTowardRanking { get; init; }

    /// <summary>Which pricing profile a session of this mode settles under. Display and support only.</summary>
    public string EconomyProfileKey { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    /// <summary>The mode a session plays when the client names none.</summary>
    public bool IsDefault { get; init; }
}

/// <summary>
/// One game's modes.
/// <para>
/// Carries <see cref="ServerTimeUtc"/> because a client must never decide whether a window is open
/// from device time — a tablet set to next year would otherwise show a mode that has not opened.
/// </para>
/// </summary>
public class GameModesResponse
{
    public string GameKey { get; init; } = string.Empty;

    public DateTime ServerTimeUtc { get; init; }

    public IReadOnlyList<GameModeDto> Modes { get; init; } = [];
}

/// <summary>
/// A mode as an <b>author</b> needs it: every field the save request takes, with names in every
/// language rather than resolved into one.
/// <para>
/// The same two-reader split as games, for the same reason: the save is a full replace, so a form
/// filled from the one-language client read would send a single language back and delete the rest.
/// </para>
/// </summary>
public class GameModeAdminDto
{
    public Guid ModeId { get; init; }
    public Guid GameId { get; init; }
    public string GameKey { get; init; } = string.Empty;
    public string ModeKey { get; init; } = string.Empty;

    public IReadOnlyList<string> Topologies { get; init; } = [];
    public int MinPlayers { get; init; }
    public int MaxPlayers { get; init; }

    public bool IsActive { get; init; }
    public bool IsDefault { get; init; }

    public DateTime? AvailableFromUtc { get; init; }
    public DateTime? AvailableToUtc { get; init; }

    public bool RequiresEntitlement { get; init; }
    public Guid? EntitlementProductId { get; init; }
    public string? EntitlementSku { get; init; }

    public int MinGradeOrder { get; init; }

    public bool CountsTowardMastery { get; init; }
    public bool SettlesEconomy { get; init; }
    public bool CountsTowardRanking { get; init; }

    public Guid? EconomyProfileId { get; init; }
    public string EconomyProfileKey { get; init; } = string.Empty;

    public int SortOrder { get; init; }

    /// <summary>The name in the caller's language, for drawing a row. Never what a form fills from.</summary>
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Guid LangId { get; init; }

    /// <summary>Deliberately the save request's own translation type: what is read back is what has to be sent.</summary>
    public IReadOnlyList<GameModeTranslationRequest> Translations { get; init; } = [];

    /// <summary>How many runs have been recorded in this mode, so a console can say what deleting it would lose.</summary>
    public int RunCount { get; init; }
}

public class SaveGameModeRequest
{
    [Required]
    public Guid GameId { get; set; }

    /// <summary>
    /// Stable machine key. Immutable once published — an update that changes it is refused, because
    /// every run, result and board already recorded points at the old one.
    /// </summary>
    [Required, MaxLength(128)]
    public string ModeKey { get; set; } = string.Empty;

    /// <summary><c>["solo","versus"]</c>. At least one, and every token must be one this server knows.</summary>
    [Required, MinLength(1, ErrorMessage = "A mode must be playable in at least one topology.")]
    public List<string> Topologies { get; set; } = [];

    [Range(1, 64)]
    public int MinPlayers { get; set; } = 1;

    [Range(1, 64)]
    public int MaxPlayers { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    /// <summary>Setting this moves the default off whatever mode of that game currently holds it.</summary>
    public bool IsDefault { get; set; }

    public DateTime? AvailableFromUtc { get; set; }
    public DateTime? AvailableToUtc { get; set; }

    public bool RequiresEntitlement { get; set; }
    public Guid? EntitlementProductId { get; set; }

    [Range(0, 100)]
    public int MinGradeOrder { get; set; }

    public bool CountsTowardMastery { get; set; } = true;
    public bool SettlesEconomy { get; set; } = true;
    public bool CountsTowardRanking { get; set; } = true;

    /// <summary>Null uses the platform's default profile.</summary>
    public Guid? EconomyProfileId { get; set; }

    [Range(0, 10_000)]
    public int SortOrder { get; set; }

    [Required, MinLength(1, ErrorMessage = "A name is required for every configured language.")]
    public List<GameModeTranslationRequest> Translations { get; set; } = [];
}

public class GameModeTranslationRequest
{
    [Required]
    public Guid LangId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;
}

/// <summary>What deleting a mode would destroy, returned with the refusal.</summary>
public class GameModeDeletionImpact
{
    public int Runs { get; set; }
    public int Results { get; set; }
    public int Events { get; set; }

    public bool HasHistory => Runs > 0 || Results > 0 || Events > 0;

    public string Describe() => HasHistory
        ? $"{Runs} run(s), {Results} recorded result(s) and {Events} event(s)"
        : "no recorded history";
}
