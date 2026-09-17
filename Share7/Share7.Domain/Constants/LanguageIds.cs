namespace Share7.Domain.Constants;

/// <summary>
/// Fixed ids for the seeded content languages. Stable across environments so the admin UI
/// and the Unity client can hard-code them if needed.
/// </summary>
public static class LanguageIds
{
    public static readonly Guid English = Guid.Parse("9c4d7f2a-3e51-4b6c-8d0a-2f7b1e5c9a34");
    public static readonly Guid Arabic = Guid.Parse("4b8e1d6f-7a29-4c35-9e10-6d3f8b2a5c71");
}
