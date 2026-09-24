namespace Share7.Domain.Constants;

/// <summary>
/// Fixed ids for the curriculum and competency rows the migration projects the existing tree into.
/// <para>
/// Seeded rather than generated because the backfill has to be re-runnable and because a restored
/// database, a fresh test database and the production one must agree on which curriculum version
/// the legacy Egyptian tree belongs to. A generated id would make every environment's evidence
/// point somewhere different.
/// </para>
/// </summary>
public static class EducationIds
{
    /// <summary>The Egyptian Ministry of Education, as the publisher of the existing content.</summary>
    public static readonly Guid EgyptianAuthority =
        Guid.Parse("e5b1c3d7-6a48-4f92-b0de-7c2a9f1863b5");

    /// <summary>The national curriculum the current Grade → Lesson tree represents.</summary>
    public static readonly Guid EgyptianNationalCurriculum =
        Guid.Parse("f6c2d4e8-7b59-4a03-a1ef-8d3b0a2974c6");

    /// <summary>
    /// "As of migration" — the version every legacy node and every historical response belongs to.
    /// Not authoritative yet: it is a projection of the typed tables until the last reader moves.
    /// </summary>
    public static readonly Guid EgyptianNationalAsMigrated =
        Guid.Parse("a7d3e5f9-8c60-4b14-92a0-9e4c1b308577");

    /// <summary>
    /// The bootstrap competency framework: one placeholder target per lesson, so that measurement
    /// can begin before a real framework has been authored. Every target in it is flagged
    /// <c>IsPlaceholder</c> and is excluded from aggregation and projection — §20.5.
    /// </summary>
    public static readonly Guid PlaceholderFramework =
        Guid.Parse("b8e4f6a0-9d71-4c25-83b1-0f5d2c419688");
}
