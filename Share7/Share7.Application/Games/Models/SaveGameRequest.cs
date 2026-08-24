using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Games.Models;

/// <summary>
/// Body for creating or replacing a game. Like the curriculum nodes, a display name and
/// description are required for <b>every</b> configured language — a game with a missing
/// translation would show up blank for those students.
/// <para>
/// Scenes are not authored here. They are client content, named by the Unity
/// <c>MiniGameDefinitionSO</c> that Addressables delivers alongside them; a scene identity typed
/// into an admin form is a second source of truth the server can neither resolve nor check.
/// </para>
/// </summary>
public class SaveGameRequest
{
    /// <summary>
    /// Readable stable key, e.g. "game.runner". Must be unique across games, and must equal the
    /// <c>gameId</c> on the Unity definition — that equality is the whole join between the two
    /// catalogues.
    /// </summary>
    [Required, MaxLength(64)]
    public string GameKey { get; set; } = string.Empty;

    [Range(1, 64)]
    public int MinPlayers { get; set; } = 1;

    [Range(1, 64)]
    public int MaxPlayers { get; set; } = 2;

    [Range(0, 600)]
    public float ReadyTimeoutSeconds { get; set; } = 20f;

    public bool SupportsSinglePlayer { get; set; } = true;
    public bool SupportsMultiplayer { get; set; } = true;
    public bool UseLobby { get; set; } = true;
    public bool UseMatchmaking { get; set; } = true;

    /// <summary>
    /// Clear to pull the game from every client without a store release. Prefer this over deleting
    /// a game that has progress against it.
    /// </summary>
    public bool IsActive { get; set; } = true;

    [Required, MinLength(1, ErrorMessage = "At least one translation is required.")]
    public List<GameTranslationRequest> Translations { get; set; } = [];
}

public class GameTranslationRequest
{
    [Required]
    public Guid LangId { get; set; }

    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;
}
