namespace Share7.Application.Games.Models;

/// <summary>
/// A game as the Unity client consumes it. Field names deliberately mirror
/// <c>MiniGameDefinitionSO</c> (<c>gameId</c>, <c>displayName</c>, <c>lobbyScene</c>, ...) so the
/// client can deserialize straight into its existing model without remapping.
/// </summary>
public class GameDto
{
    /// <summary>Unity's <c>gameId</c>. Serialized as a string.</summary>
    public Guid GameId { get; set; }

    public string GameKey { get; set; } = string.Empty;

    /// <summary>Resolved into the caller's content language; <see cref="LangId"/> says which.</summary>
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid LangId { get; set; }

    /// <summary>Unity build indices. Superseded by the scene addresses below; still served so
    /// existing clients keep working.</summary>
    public int LobbyScene { get; set; }
    public int GameplayScene { get; set; }

    /// <summary>
    /// Addressables scene addresses. <b>Null means this game still uses the build indices
    /// above</b> — that is what a client switches on. A downloadable mini-game has no build
    /// index to give, so scene identity has to be a key the content system can resolve.
    /// </summary>
    public string? LobbySceneAddress { get; set; }
    public string? GameplaySceneAddress { get; set; }

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public float ReadyTimeoutSeconds { get; set; }

    public bool SupportsSinglePlayer { get; set; }
    public bool SupportsMultiplayer { get; set; }
    public bool UseLobby { get; set; }
    public bool UseMatchmaking { get; set; }

    public bool IsActive { get; set; }
}
