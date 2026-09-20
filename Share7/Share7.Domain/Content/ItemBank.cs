using Share7.Domain.Evidence;

namespace Share7.Domain.Content;

/// <summary>
/// An ownership and trust scope for items. Not a folder — a statement about how far the content
/// inside may be relied upon.
/// <para>
/// <see cref="MaxEvidenceStrength"/> is the mechanism: it is a **ceiling applied to derived
/// evidence strength**, so an unreviewed teacher-authored item produces <c>Practice</c>-class
/// evidence however controlled the conditions were, and can never move a national-exam projection
/// until somebody qualified has looked at it. The ceiling is applied where strength is computed
/// (the observation projection), not stored on the response — strength stays derived.
/// </para>
/// </summary>
public class ItemBank
{
    public Guid Id { get; set; }

    /// <summary>Stable, human-readable. <c>platform.curriculum</c> for the seeded bank.</summary>
    public string BankKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ItemBankOwnerScope OwnerScope { get; set; }

    /// <summary>The organization or user that owns it. Null for the platform's own bank.</summary>
    public Guid? OwnerId { get; set; }

    public ItemBankReviewPolicy ReviewPolicy { get; set; }

    /// <summary>
    /// The trust ceiling. Evidence derived from an item in this bank is clamped to this class
    /// however controlled the administration was.
    /// </summary>
    public EvidenceStrength MaxEvidenceStrength { get; set; } = EvidenceStrength.Assessment;

    /// <summary>
    /// Whether statistics from this bank's items join the global pool. Never for user banks: one
    /// teacher's class is not a calibration sample, and pooling it would move everybody's numbers.
    /// </summary>
    public bool PoolsStatisticsGlobally { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Item> Items { get; set; } = new List<Item>();
}
