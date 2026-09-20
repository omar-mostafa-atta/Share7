namespace Share7.Domain.Constants;

/// <summary>
/// Fixed ids for the content scopes the code depends on existing.
/// <para>
/// Seeded for the same reason as <see cref="EvidenceContractIds"/>: <c>Item.ItemBankId</c> is
/// non-nullable and the sheet importer mints items on every publish, so a deployment without this
/// row cannot accept content at all.
/// </para>
/// </summary>
public static class ContentIds
{
    /// <summary>
    /// Share7's own curriculum bank — everything the sheet importer has ever published. Editorially
    /// reviewed, pools globally, may reach Assessment strength.
    /// </summary>
    public static readonly Guid PlatformCurriculumBank =
        Guid.Parse("d3a9e6cb-418a-4f5d-b079-2c6e0f37b4d8");
}
