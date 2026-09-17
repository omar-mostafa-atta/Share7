namespace Share7.Application.Equipment.Models;

/// <summary>
/// The <c>PUT</c> body. **Deliberately not the same shape as the response.**
/// <para>
/// Each entry carries its own <c>colorKey</c>, so a colour can only ever be attached to a cosmetic
/// that is actually being equipped. The earlier shape took a separate <c>colors[]</c> list keyed by
/// <c>cosmeticKey</c>, which let a client colour something it was not wearing — <c>equip
/// Armor_gold, colour "Test"</c> was accepted and stored, describing an outfit that does not exist.
/// Nesting the colour makes that unrepresentable rather than merely discouraged.
/// </para>
/// <para>
/// The response still splits the two apart into <c>equipped[]</c> and <c>colors[]</c> — those are
/// computed from the stored rows, not stored in that shape.
/// </para>
/// <para>
/// Any <c>userId</c> or <c>updatedAtUtc</c> in the body is ignored: identity comes from the token
/// and the timestamp is stamped by the server. Neither appears here so that is evident from the
/// type rather than a rule to remember.
/// </para>
/// </summary>
public class UpdateEquipmentRequest
{
    /// <summary>
    /// <c>"Male"</c> / <c>"Female"</c>, case-insensitive. Null or empty means <c>Male</c>, the
    /// documented default. Anything else is a <c>422</c>.
    /// <para>
    /// A <see cref="string"/> rather than the enum on purpose: bound as an enum, an unknown value
    /// would be refused by the model binder as a framework-shaped <c>400</c>, while every other bad
    /// field here answers <c>422</c> with this endpoint's error envelope.
    /// </para>
    /// </summary>
    public string? BodyType { get; set; }

    /// <summary>
    /// The complete outfit. Null is treated as empty — "wearing nothing" is a real, storable state,
    /// so an absent array cannot mean "leave what is stored alone"; a save always replaces the
    /// whole outfit.
    /// </summary>
    public List<EquipmentSlotInput>? Equipped { get; set; }
}

/// <summary>One slot of the outfit being saved: what is worn there, and in what colour.</summary>
public class EquipmentSlotInput
{
    /// <summary>Required. Unique within the request, compared case-insensitively.</summary>
    public string SlotKey { get; set; } = string.Empty;

    /// <summary>Required.</summary>
    public string CosmeticKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional — a cosmetic may be worn with no colour chosen. When present it must satisfy the
    /// same length and character rules as the other keys.
    /// </summary>
    public string? ColorKey { get; set; }
}
