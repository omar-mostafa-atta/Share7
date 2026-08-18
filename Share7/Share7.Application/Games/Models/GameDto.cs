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

    public int LobbyScene { get; set; }
    public int GameplayScene { get; set; }

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public float ReadyTimeoutSeconds { get; set; }

    public bool SupportsSinglePlayer { get; set; }
    public bool SupportsMultiplayer { get; set; }
    public bool UseLobby { get; set; }
    public bool UseMatchmaking { get; set; }

    public bool IsActive { get; set; }
}
