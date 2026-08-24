namespace Share7.Domain.Games;

/// <summary>
/// A playable mini-game, as the platform's catalogue of them. Pairs with Unity's
/// <c>MiniGameDefinitionSO</c>, and **this table is the authority over what it covers** —
/// matchmaking has to enforce player counts server-side, so the database wins over the
/// ScriptableObject if the two ever disagree.
/// <para>
/// <b>What it covers is identity, availability and policy — never content.</b> This row answers
/// "which games exist, is this one offered, and what are its rules". It deliberately does not
/// answer "how does Unity run one": scenes, network prefabs and environments are client content,
/// resolved from the ScriptableObject, which is itself delivered through Addressables. The server
/// cannot see the client's content catalogue, so a scene identity stored here could never be
/// validated, never be resolved, and could only ever disagree with the catalogue that actually
/// loads it. Scene columns were removed for exactly that reason — see
/// <c>20260824_DropGameSceneColumns</c>.
/// </para>
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

    /// <summary>
    /// Readable stable key, e.g. "game.runner". Unique.
    /// <para>
    /// This is the join to Unity: it equals <c>MiniGameDefinitionSO.gameId</c>, which is also the
    /// definition's Addressables address. It is the only field the two sides must agree on, which
    /// is why it is the only string the client ever matches against.
    /// </para>
    /// </summary>
    public string GameKey { get; set; } = string.Empty;

    public int MinPlayers { get; set; } = 1;
    public int MaxPlayers { get; set; } = 2;
    public float ReadyTimeoutSeconds { get; set; } = 20f;

    public bool SupportsSinglePlayer { get; set; } = true;
    public bool SupportsMultiplayer { get; set; } = true;
    public bool UseLobby { get; set; } = true;
    public bool UseMatchmaking { get; set; } = true;

    /// <summary>
    /// Whether the platform currently offers this game.
    /// <para>
    /// The one field here that a shipped build cannot reproduce on its own: clearing it pulls a
    /// broken mini-game from every client without a store release. The client hides an inactive
    /// game and refuses to open a session for one.
    /// </para>
    /// </summary>
    public bool IsActive { get; set; } = true;

    public ICollection<GameTranslation> Translations { get; set; } = new List<GameTranslation>();
}
