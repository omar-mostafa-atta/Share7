namespace Share7.Application.Games.Models;

/// <summary>
/// A game as the Unity client consumes it: identity, localized text, availability and the
/// capability flags matchmaking enforces.
/// <para>
/// <b>No scenes.</b> A client resolves scenes from its own <c>MiniGameDefinitionSO</c>, which
/// arrives through the same Addressables catalogue as the scenes themselves — so there is never a
/// moment where a client knows a game exists, can download its content, but needs this response to
/// tell it what to load. Serving a scene identity here could only add a second source of truth for
/// a value the server cannot resolve or validate.
/// </para>
/// <para>
/// Field names deliberately mirror <c>MiniGameDefinitionSO</c> (<c>gameId</c>, <c>displayName</c>,
/// <c>minPlayers</c>, ...) so the client deserializes straight into its existing model without
/// remapping.
/// </para>
/// </summary>
public class GameDto
{
    /// <summary>Unity's <c>gameId</c>. Serialized as a string.</summary>
    public Guid GameId { get; set; }

    /// <summary>The join to the Unity catalogue: equals <c>MiniGameDefinitionSO.gameId</c>.</summary>
    public string GameKey { get; set; } = string.Empty;

    /// <summary>Resolved into the caller's content language; <see cref="LangId"/> says which.</summary>
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid LangId { get; set; }

    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public float ReadyTimeoutSeconds { get; set; }

    public bool SupportsSinglePlayer { get; set; }
    public bool SupportsMultiplayer { get; set; }
    public bool UseLobby { get; set; }
    public bool UseMatchmaking { get; set; }

    /// <summary>False hides the game and refuses new sessions on the client.</summary>
    public bool IsActive { get; set; }
}
