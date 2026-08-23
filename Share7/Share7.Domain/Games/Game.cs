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
    /// <para>
    /// Superseded by <see cref="LobbySceneAddress"/> / <see cref="GameplaySceneAddress"/>, and
    /// kept only so clients still reading them do not break. Retire them once no shipped build
    /// depends on them.
    /// </para>
    /// </summary>
    public int LobbyScene { get; set; }
    public int GameplayScene { get; set; }

    /// <summary>
    /// Addressables scene addresses, e.g. <c>Assets/Games/Runner/Scenes/GameRunner.unity</c>.
    /// <para>
    /// These exist because a build index cannot name a scene that is not in the build. A
    /// mini-game whose scenes are downloaded on demand has no index to give, so scene identity
    /// has to be a key the content system can resolve. The server never resolves one — it cannot
    /// see the client's content catalogue — it stores and serves what the catalogue was authored
    /// with.
    /// </para>
    /// <para>
    /// <b>Null means this game still uses the build indices above.</b> That is the discriminator
    /// clients switch on, which is why these are nullable rather than empty strings: "not
    /// authored yet" and "authored as blank" must not look the same during the migration.
    /// </para>
    /// </summary>
    public string? LobbySceneAddress { get; set; }
    public string? GameplaySceneAddress { get; set; }

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
