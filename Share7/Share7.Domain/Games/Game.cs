namespace Share7.Domain.Games;

/// <summary>
/// A playable mini-game. Mirrors Unity's <c>MiniGameDefinitionSO</c>, and **this table is the
/// authority** — matchmaking has to enforce player counts server-side, so the database wins
/// over the ScriptableObject if the two ever disagree.
/// <para>
/// Display name and description live in <see cref="Translations"/>. One game is one row with
/// one id in every language: two rows per language would give the same game two ids and split
/// its progress in half.
/// </para>
/// </summary>
public class Game
{
    /// <summary>Unity's <c>gameId</c>. Sent over the wire as a string; stored as a Guid.</summary>
    public Guid Id { get; set; }

    /// <summary>Readable stable key, e.g. "subway_runner". Unique.</summary>
    public string GameKey { get; set; } = string.Empty;

    /// <summary>
    /// Unity build indices. Stored here by request, but they are client build artifacts — a
    /// rebuild that renumbers scenes will silently disagree with these values.
    /// </summary>
    public int LobbyScene { get; set; }
    public int GameplayScene { get; set; }

    public int MinPlayers { get; set; } = 1;
    public int MaxPlayers { get; set; } = 2;
    public float ReadyTimeoutSeconds { get; set; } = 20f;

    public bool SupportsSinglePlayer { get; set; } = true;
    public bool SupportsMultiplayer { get; set; } = true;
    public bool UseLobby { get; set; } = true;
    public bool UseMatchmaking { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public ICollection<GameTranslation> Translations { get; set; } = new List<GameTranslation>();
}
