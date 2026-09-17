using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Play.Models;

/// <summary>
/// One world of one game, and whether this player may run in it.
/// <para>
/// <b>Ownership is answered here rather than computed on the device.</b> The client can compute the
/// free floor on its own — and does, so a world it already has never disappears because a refresh
/// came back empty — but level, grade, purchase and event grants are all server facts.
/// </para>
/// </summary>
public class GameWorldDto
{
    public Guid WorldId { get; init; }

    /// <summary>The client's own environment id, e.g. <c>runner.env.desert</c>.</summary>
    public string WorldKey { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary><c>free</c>, <c>purchase</c>, <c>level</c>, <c>grade</c> or <c>reward</c>.</summary>
    public string UnlockKind { get; init; } = string.Empty;

    /// <summary>Whether this account may play in it right now.</summary>
    public bool Owned { get; init; }

    /// <summary>
    /// Set when ownership is temporary — an event that lends its world for the duration. Null means
    /// permanent, which is what every other kind of ownership is.
    /// </summary>
    public DateTime? OwnedUntilUtc { get; init; }

    /// <summary>The product it is sold or granted under, when there is one.</summary>
    public Guid? ProductId { get; init; }

    /// <summary>That product's key, so a shop deep link needs no second lookup.</summary>
    public string? Sku { get; init; }

    /// <summary>An offer currently selling it, when one exists. Null means it is not on sale today.</summary>
    public Guid? OfferId { get; init; }

    public int MinLevel { get; init; }
    public int MinGradeOrder { get; init; }

    public int SortOrder { get; init; }

    /// <summary>The world a session runs in when nothing was chosen. Always free.</summary>
    public bool IsDefault { get; init; }
}

/// <summary>One game's worlds, as one player sees them.</summary>
public class GameWorldsResponse
{
    public string GameKey { get; init; } = string.Empty;

    public DateTime ServerTimeUtc { get; init; }

    public IReadOnlyList<GameWorldDto> Worlds { get; init; } = [];
}

/// <summary>A world in the authoring shape: every language, and the policy fields a form edits.</summary>
public class GameWorldAdminDto
{
    public Guid WorldId { get; init; }
    public Guid GameId { get; init; }
    public string GameKey { get; init; } = string.Empty;
    public string WorldKey { get; init; } = string.Empty;

    public string UnlockKind { get; init; } = string.Empty;
    public Guid? ProductId { get; init; }
    public string? Sku { get; init; }

    public int MinLevel { get; init; }
    public int MinGradeOrder { get; init; }
    public int SortOrder { get; init; }

    public bool IsActive { get; init; }
    public bool IsDefault { get; init; }

    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Guid LangId { get; init; }

    public IReadOnlyList<GameWorldTranslationRequest> Translations { get; init; } = [];
}

public class SaveGameWorldRequest
{
    [Required]
    public Guid GameId { get; set; }

    /// <summary>
    /// The Unity environment id this row governs. Immutable once published — entitlements, events
    /// and runs all name it.
    /// </summary>
    [Required, MaxLength(128)]
    public string WorldKey { get; set; } = string.Empty;

    /// <summary><c>free</c>, <c>purchase</c>, <c>level</c>, <c>grade</c> or <c>reward</c>.</summary>
    [Required, MaxLength(16)]
    public string UnlockKind { get; set; } = "free";

    /// <summary>Required for <c>purchase</c> and <c>reward</c>; refused for the rest.</summary>
    public Guid? ProductId { get; set; }

    [Range(0, 1000)]
    public int MinLevel { get; set; }

    [Range(0, 100)]
    public int MinGradeOrder { get; set; }

    [Range(0, 10_000)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>A game's fallback world. Must be free, and setting it moves the flag off whatever held it.</summary>
    public bool IsDefault { get; set; }

    [Required, MinLength(1, ErrorMessage = "A name is required for every configured language.")]
    public List<GameWorldTranslationRequest> Translations { get; set; } = [];
}

public class GameWorldTranslationRequest
{
    [Required]
    public Guid LangId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;
}
