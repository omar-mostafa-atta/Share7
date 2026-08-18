namespace Share7.Domain.Commerce;

/// <summary>
/// What category of thing a <see cref="Product"/> is, and therefore how the **client** should
/// interpret the references its grants hand over — a cosmetic id, an Addressables pack id.
/// <para>
/// This replaces the old <c>GrantKind</c> enum, and it moved **up a level** with the change: kind is
/// now a property of the product rather than of each individual grant. A product is one kind of
/// thing, and everything it hands over is read in that light. A bundle mixing categories is authored
/// as two products.
/// </para>
/// <para>
/// It is a table rather than an enum so an admin can add a category without a deployment. The
/// trade-off is that the backend can no longer validate the vocabulary — <c>Name</c> is whatever was
/// typed, and it reaches the client as the grant's <c>kind</c>, so it has to match what the client
/// actually looks for. See <see cref="Name"/>.
/// </para>
/// </summary>
public class ProductKind
{
    public Guid Id { get; set; }

    /// <summary>
    /// The category, e.g. <c>Cosmetic</c>. **This is contract**: it is normalised to
    /// <c>SCREAMING_SNAKE</c> and sent to the client as each grant's <c>kind</c>, so
    /// <c>Cosmetic</c>, <c>cosmetic</c> and <c>COSMETIC</c> all arrive as <c>COSMETIC</c> and mean
    /// the same thing. Unique, ignoring case.
    /// <para>
    /// **Deliberately not translated**, unlike every other name in this system. <c>COSMETIC</c> has
    /// to mean the same thing to an Arabic client as to an English one; the human label lives in
    /// <see cref="Translations"/>. Renaming this changes what every product of that kind reports,
    /// and there is no catalogue here to check it against, so a name the client does not recognise
    /// is undetectable on this side.
    /// </para>
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The admin-facing label and note, one row per language. Never sent to the client — Unity gets
    /// <see cref="Name"/> normalised, and owns its own display text for it.
    /// </summary>
    public ICollection<ProductKindTranslation> Translations { get; set; } = new List<ProductKindTranslation>();

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
