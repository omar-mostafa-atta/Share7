namespace Share7.Domain.Constants;

/// <summary>
/// Fixed ids for the seeded grades of the Egyptian pre-university system. Stable across
/// environments, in the same spirit as <see cref="LanguageIds"/>, so the admin page and the
/// Unity client can refer to a grade without a lookup.
/// <para>
/// The names themselves live in <c>GradeTranslations</c> — a grade row carries no text.
/// </para>
/// </summary>
public static class GradeIds
{
    public static readonly Guid Kg1 = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    public static readonly Guid Kg2 = Guid.Parse("a0000000-0000-4000-8000-000000000002");

    public static readonly Guid PrimaryOne = Guid.Parse("a0000000-0000-4000-8000-000000000003");
    public static readonly Guid PrimaryTwo = Guid.Parse("a0000000-0000-4000-8000-000000000004");
    public static readonly Guid PrimaryThree = Guid.Parse("a0000000-0000-4000-8000-000000000005");
    public static readonly Guid PrimaryFour = Guid.Parse("a0000000-0000-4000-8000-000000000006");
    public static readonly Guid PrimaryFive = Guid.Parse("a0000000-0000-4000-8000-000000000007");
    public static readonly Guid PrimarySix = Guid.Parse("a0000000-0000-4000-8000-000000000008");

    public static readonly Guid PreparatoryOne = Guid.Parse("a0000000-0000-4000-8000-000000000009");
    public static readonly Guid PreparatoryTwo = Guid.Parse("a0000000-0000-4000-8000-00000000000a");
    public static readonly Guid PreparatoryThree = Guid.Parse("a0000000-0000-4000-8000-00000000000b");

    public static readonly Guid SecondaryOne = Guid.Parse("a0000000-0000-4000-8000-00000000000c");
    public static readonly Guid SecondaryTwo = Guid.Parse("a0000000-0000-4000-8000-00000000000d");
    public static readonly Guid SecondaryThree = Guid.Parse("a0000000-0000-4000-8000-00000000000e");
}
