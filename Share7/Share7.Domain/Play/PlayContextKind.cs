namespace Share7.Domain.Play;

/// <summary>
/// Why this session is being played, and therefore what it may be worth.
/// <para>
/// **The axis the server prices a session by, alongside the mode.** The same mode played for the
/// curriculum, for fun, as practice or inside an event is the same rules and four different
/// settlements — so neither half can decide on its own, and the server takes the intersection of
/// what the mode allows and what the context allows. See <c>PlayAccounting</c>.
/// </para>
/// <para>
/// A client selects a context; it never asserts what the context is worth. There is no request
/// shape in this domain in which a client can name a multiplier, and that absence is the contract.
/// </para>
/// </summary>
public enum PlayContextKind
{
    /// <summary>
    /// The lesson tree. The only context that can move mastery — completion, unlocks and the
    /// progress a parent sees are all statements about the curriculum, and nothing else may write them.
    /// </summary>
    Curriculum = 0,

    /// <summary>
    /// Chosen for its own sake, off the tree. Pays and ranks like the curriculum; records no progress,
    /// because "played it again for fun" is not evidence of having learned anything new.
    /// </summary>
    FreePlay = 1,

    /// <summary>
    /// Deliberately worth nothing. Retrying questions already answered wrongly must stay free of
    /// consequence, or a child learns that practising costs them their record.
    /// </summary>
    Practice = 2,

    /// <summary>An authored event. Ranks on that event's own board and settles under its economy profile.</summary>
    Event = 3,

    /// <summary>
    /// Set by a teacher. **Declared, not implemented** — there is no class or enrolment relation in
    /// this schema to resolve one against, so a session naming it is refused rather than silently
    /// treated as curriculum.
    /// </summary>
    Assignment = 4
}

/// <summary>The wire tokens for <see cref="PlayContextKind"/>, and parsing that refuses what it does not know.</summary>
public static class PlayContextTokens
{
    public const string Curriculum = "curriculum";
    public const string FreePlay = "freeplay";
    public const string Practice = "practice";
    public const string Event = "event";
    public const string Assignment = "assignment";

    public static string ToToken(PlayContextKind kind) => kind switch
    {
        PlayContextKind.Curriculum => Curriculum,
        PlayContextKind.FreePlay => FreePlay,
        PlayContextKind.Practice => Practice,
        PlayContextKind.Event => Event,
        PlayContextKind.Assignment => Assignment,
        _ => Curriculum
    };

    public static bool TryParse(string? token, out PlayContextKind kind)
    {
        switch ((token ?? string.Empty).Trim().ToLowerInvariant())
        {
            case Curriculum: kind = PlayContextKind.Curriculum; return true;
            case FreePlay: kind = PlayContextKind.FreePlay; return true;
            case Practice: kind = PlayContextKind.Practice; return true;
            case Event: kind = PlayContextKind.Event; return true;
            case Assignment: kind = PlayContextKind.Assignment; return true;
            default: kind = PlayContextKind.Curriculum; return false;
        }
    }
}
