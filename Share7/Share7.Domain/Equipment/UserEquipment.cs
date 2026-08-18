namespace Share7.Domain.Equipment;

/// <summary>
/// One equipped item, in table <c>Equipments</c>. A player wearing two things has two rows.
/// <para>
/// **One row per (user, slot)**, enforced by a unique index. That is the rule stated as "the same
/// user with the same slot key cannot be in the table more than once": a save updates the row
/// already holding that slot rather than adding a second one.
/// </para>
/// <para>
/// <see cref="BodyType"/> and <see cref="UpdatedAtUtc"/> are per *player*, not per item, so every
/// row belonging to one user carries the same value for both. They are written together on every
/// save, so the copies cannot drift.
/// </para>
/// <para>
/// **The no-items row.** A player who takes everything off keeps exactly one row with
/// <see cref="SlotKey"/>, <see cref="CosmeticKey"/> and <see cref="ColorKey"/> all null. Without
/// it, "took everything off" and "has never saved" would both be zero rows, and the client could
/// not tell them apart — the distinction <see cref="UpdatedAtUtc"/> exists to carry. The unique
/// index gives this for free: SQL Server treats nulls as equal for uniqueness, so a user can hold
/// at most one such row.
/// </para>
/// <para>
/// **Keys are never validated against a catalogue.** There is no backend cosmetic catalogue by
/// decision — cosmetics are Unity assets — so unknown keys are stored and handed back verbatim.
/// That is what lets content ship ahead of a backend deploy. They are still bounded in length,
/// count and character set, because without that this is an unbounded free-text store keyed by
/// client-supplied strings on a children's product.
/// </para>
/// </summary>
public class UserEquipment
{
    public Guid Id { get; set; }

    /// <summary>
    /// Owner. Cascades from <c>AspNetUsers</c>, so a deleted account takes its outfit with it
    /// without the deletion sweep needing to know this table exists.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The avatar body. Per player rather than per item — every row of one user's outfit repeats
    /// the same value, which is what keeps it readable off any single row including the no-items one.
    /// </summary>
    public BodyType BodyType { get; set; } = BodyType.Male;

    /// <summary>
    /// Which slot this item occupies. **Null only on the no-items row**, which records that the
    /// player has saved an empty outfit.
    /// </summary>
    public string? SlotKey { get; set; }

    /// <summary>What is worn in that slot. Null only on the no-items row.</summary>
    public string? CosmeticKey { get; set; }

    /// <summary>
    /// The colour chosen for <see cref="CosmeticKey"/>. Optional on a real item — a cosmetic may
    /// be worn with no colour picked — and always null on the no-items row.
    /// <para>
    /// Colour lives on the item row, so it belongs to a cosmetic that is actually equipped. The
    /// consequence is deliberate: unequipping an item discards its colour rather than remembering
    /// it for next time.
    /// </para>
    /// </summary>
    public string? ColorKey { get; set; }

    /// <summary>
    /// When the outfit was last saved. Per player, so every row carries the same stamp.
    /// <para>
    /// **This is the field the whole feature turns on.** The client uses its absence — meaning no
    /// rows at all — to tell "never dressed" (upload whatever the device is wearing) from
    /// "deliberately wearing nothing" (undress the avatar). Both look like an empty
    /// <c>equipped</c> list, which is why the no-items row has to exist.
    /// </para>
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>True for the marker row that records an intentionally empty outfit.</summary>
    public bool IsNoItemsRow => SlotKey is null;
}
