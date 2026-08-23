using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Games.Models;

/// <summary>
/// Body for creating or replacing a game. Like the curriculum nodes, a display name and
/// description are required for <b>every</b> configured language — a game with a missing
/// translation would show up blank for those students.
/// </summary>
public class SaveGameRequest
{
    /// <summary>Readable stable key, e.g. "subway_runner". Must be unique across games.</summary>
    [Required, MaxLength(64)]
    public string GameKey { get; set; } = string.Empty;

    public int LobbyScene { get; set; }
    public int GameplayScene { get; set; }

    /// <summary>
    /// Addressables scene addresses, e.g. <c>Assets/Games/Runner/Scenes/GameRunner.unity</c>.
    /// Leave both null to keep using the build indices above.
    /// <para>
    /// <see cref="GameplaySceneAddress"/> is the anchor: supplying it puts the game on
    /// addressable scenes, and <see cref="LobbySceneAddress"/> is then required whenever
    /// <see cref="UseLobby"/> is set. A lobby address on its own is rejected — nothing would
    /// read it.
    /// </para>
    /// </summary>
    [MaxLength(256)]
    public string? LobbySceneAddress { get; set; }

    [MaxLength(256)]
    public string? GameplaySceneAddress { get; set; }

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
