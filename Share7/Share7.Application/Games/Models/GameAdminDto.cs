namespace Share7.Application.Games.Models;

/// <summary>
/// A game as an <b>author</b> needs to see it: every field <see cref="SaveGameRequest"/> takes,
/// plus its id, with names in <b>every</b> language rather than resolved into one.
/// <para>
/// <see cref="GameDto"/> deliberately cannot serve this. It resolves a single translation from the
/// caller's token, which is exactly right for the Unity client and exactly wrong for an edit form:
/// <c>PUT</c> is a full replace, so a form filled from a one-language read would send that language
/// back on its own and silently delete every other one. Two readers because there are two
/// audiences, not because the shape drifted.
/// </para>
/// <para>
/// Like the read model, it carries no scenes. An author cannot usefully edit a value the server
/// cannot resolve and no client reads — offering the field would only invite someone to author a
/// scene identity that disagrees with the content catalogue.
/// </para>
/// </summary>
public class GameAdminDto
{
    public Guid GameId { get; init; }

    public string GameKey { get; init; } = string.Empty;

    public int MinPlayers { get; init; }
    public int MaxPlayers { get; init; }
    public float ReadyTimeoutSeconds { get; init; }

    public bool SupportsSinglePlayer { get; init; }
    public bool SupportsMultiplayer { get; init; }
    public bool UseLobby { get; init; }
    public bool UseMatchmaking { get; init; }

    public bool IsActive { get; init; }

    /// <summary>
    /// One entry per language that has a name — deliberately the same type the save request takes,
    /// so what is read back is literally what has to be sent again.
    /// </summary>
    public IReadOnlyList<GameTranslationRequest> Translations { get; init; } = [];
}
