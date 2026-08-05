namespace Share7.Domain.Constants;

public static class CustomClaimTypes
{
    /// <summary>
    /// The user's content language, carried in the access token so content endpoints don't
    /// hit the database on every request. Treat it as a cache: it reflects the preference as
    /// of token issue. Changing the preference re-issues tokens, and endpoints fall back to
    /// the database when the claim is absent (tokens minted before this claim existed).
    /// </summary>
    public const string PreferredLanguage = "preferred_language";
}
