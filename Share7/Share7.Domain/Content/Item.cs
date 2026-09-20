namespace Share7.Domain.Content;

/// <summary>
/// The stable identity of a question **as a thing that exists** — across rewrites, across
/// languages, and across the curricula that reference it.
/// <para>
/// This is the fix for the identity fault in <c>Docs/EducationalArchitecture.md</c> §1.5. Before it,
/// the English and Arabic renderings of one question were two unrelated GUIDs, and republishing a
/// lesson minted a third: a child's history on a question was silently severed by switching
/// language and again by any content edit. Nothing above this row is per-language — the text is,
/// and only the text.
/// </para>
/// <para>
/// An item owns its target mappings and its statistics. It does **not** own a place in a curriculum:
/// nodes reference items through <c>NodeItemMapping</c>, so one item can serve a national chapter,
/// an IGCSE topic and a teacher's revision week while pooling one set of statistics (§10.4).
/// </para>
/// </summary>
public class Item
{
    public Guid Id { get; set; }

    public Guid ItemBankId { get; set; }
    public ItemBank? Bank { get; set; }

    /// <summary>
    /// Where this item came from, as a stable string — <c>lesson/{lessonId}/core/{rowNumber}</c> for
    /// everything migrated or published by the sheet importer.
    /// <para>
    /// **Provenance and a de-duplication key, not a live reference.** It is what lets a republish
    /// recognise "this is the same item again" and link the lineage instead of minting a stranger;
    /// the authoritative statement of where an item is *used* is <c>NodeItemMapping</c>.
    /// </para>
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    /// <summary>
    /// Administered broadly and on purpose, so that measurements taken in different places can be
    /// put on one scale.
    /// <para>
    /// **Flagged now because it cannot be flagged retroactively** — an item only works as an anchor
    /// if it was already being given across cohorts and forms while the data accumulated. §4.5.
    /// </para>
    /// </summary>
    public bool IsAnchor { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Set when the item is withdrawn. **Never deleted** — responses against it are facts about
    /// what a child did and outlive the content they were given.
    /// </summary>
    public DateTime? RetiredAtUtc { get; set; }

    public ICollection<ItemVersion> Versions { get; set; } = new List<ItemVersion>();
}
