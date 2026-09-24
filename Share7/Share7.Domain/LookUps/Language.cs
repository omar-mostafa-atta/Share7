namespace Share7.Domain.LookUps;

/// <summary>
/// A language the platform knows. The curriculum tree is shared by every language; names and
/// questions are per language.
/// <para>
/// **Languages are data.** Whether a language is one content is written in, whether a lesson may
/// go live without it, and which way it reads are columns here rather than constants in the
/// publisher — so a third language is a row, and the Studio's editor grows a column for it without
/// a code change. English and Arabic are seeded as content languages that are both required.
/// </para>
/// <para>
/// None of the new columns reach the game: <c>GET /api/languages</c> still returns id, name and
/// code, exactly as before.
/// </para>
/// </summary>
public class Language
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    /// <summary>Whether curriculum content (names, questions) is written in this language.</summary>
    public bool IsContentLanguage { get; set; } = true;

    /// <summary>
    /// Whether a lesson must have this language filled in before a Studio release can publish it.
    /// The old admin paths keep their own rules until cutover (they could always publish one
    /// language at a time).
    /// </summary>
    public bool RequiredToPublish { get; set; } = true;

    /// <summary><c>ltr</c> or <c>rtl</c>. Each content column keeps its own direction in the editor.</summary>
    public string Direction { get; set; } = LanguageDirections.LeftToRight;

    /// <summary>Column order in editors, sheets and templates. English 1, Arabic 2.</summary>
    public int SortOrder { get; set; }
}

public static class LanguageDirections
{
    public const string LeftToRight = "ltr";
    public const string RightToLeft = "rtl";
}
