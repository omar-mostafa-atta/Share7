using Share7.Domain.Equipment;

namespace Share7.Application.Equipment.Models;

/// <summary>
/// The avatar payload returned by both <c>GET</c> and <c>PUT</c>.
/// <para>
/// **<see cref="Equipped"/> and <see cref="Colors"/> are projections, not storage.** The database
/// keeps one row per equipped item carrying slot, cosmetic and colour together; these two lists are
/// derived from those rows on read. The client contract keeps them apart, so the split lives here
/// rather than in the schema.
/// </para>
/// <para>
/// The request shape is <see cref="UpdateEquipmentRequest"/>, which nests the colour inside each
/// equipped entry instead — that is what stops a colour naming a cosmetic the player is not wearing.
/// </para>
/// </summary>
public class EquipmentDto
{
    /// <summary>Serialises as <c>"Male"</c> / <c>"Female"</c> — the global string-enum converter.</summary>
    public BodyType BodyType { get; set; } = BodyType.Male;

    /// <summary>One entry per stored item row. At most one per <c>slotKey</c>.</summary>
    public IReadOnlyList<EquippedItemDto> Equipped { get; set; } = [];

    /// <summary>
    /// One entry per stored item row that has a colour — items worn without a colour chosen are
    /// simply absent. Every <c>cosmeticKey</c> here therefore also appears in
    /// <see cref="Equipped"/>, which is the invariant the nested request shape guarantees.
    /// </summary>
    public IReadOnlyList<CosmeticColorDto> Colors { get; set; } = [];

    /// <summary>
    /// When the outfit was last saved, or **null when the player has no stored outfit at all**.
    /// <para>
    /// This is the only thing separating "never dressed" from "deliberately wearing nothing" —
    /// both carry an empty <see cref="Equipped"/>. A client seeing null uploads whatever the device
    /// is wearing; a client seeing a timestamp with an empty list undresses the avatar. Returning a
    /// timestamp for a player with no stored outfit would strip every such player on next launch.
    /// </para>
    /// <para>
    /// Always carries the <c>Z</c> suffix. SQL Server hands back <c>DateTimeKind.Unspecified</c>,
    /// which the serialiser writes without a marker, so the read path re-stamps it as UTC —
    /// otherwise a client doing a naive parse would shift it by its own timezone offset.
    /// </para>
    /// </summary>
    public DateTime? UpdatedAtUtc { get; set; }
}

public class EquippedItemDto
{
    public string SlotKey { get; set; } = string.Empty;
    public string CosmeticKey { get; set; } = string.Empty;
}

public class CosmeticColorDto
{
    public string CosmeticKey { get; set; } = string.Empty;
    public string ColorKey { get; set; } = string.Empty;
}
