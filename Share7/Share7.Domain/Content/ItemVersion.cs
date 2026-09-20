using Share7.Domain.Curriculum;

namespace Share7.Domain.Content;

/// <summary>
/// One frozen revision of an <see cref="Item"/>: its key, its choice structure, its response shape.
/// **Immutable. Every response names one of these, forever.**
/// <para>
/// The localizations hang off it — today's <c>Question</c> rows, one per language, which keep their
/// original GUIDs. The key is structural and lives here, not on the localization: translating a
/// question does not change which answer is right.
/// </para>
/// </summary>
public class ItemVersion
{
    public Guid Id { get; set; }

    public Guid ItemId { get; set; }
    public Item? Item { get; set; }

    /// <summary>1-based, monotonic within the item. Matches the source sheet's publish version.</summary>
    public int VersionNumber { get; set; }

    public string ItemKindKey { get; set; } = ItemKinds.SingleChoice;

    /// <summary>JSON describing the shape of a valid response. Null for single_choice, whose shape is the choice list.</summary>
    public string? ResponseSpec { get; set; }

    /// <summary>JSON describing how a response is graded. Null for single_choice, graded by key equality.</summary>
    public string? ScoringSpec { get; set; }

    /// <summary>
    /// Does this edit preserve the item's measurement properties?
    /// <para>
    /// <c>true</c> — a typo, a formatting change, an image swap: statistics carry forward.
    /// <c>false</c> — the stem was rewritten, the key changed, a distractor replaced: statistics
    /// restart, and the earlier history stays readable without being pooled.
    /// </para>
    /// <para>
    /// **Defaults to false and must be asserted deliberately.** It cannot be inferred from a diff
    /// and cannot be recovered later; a wrong <c>true</c> silently corrupts every measurement built
    /// on the item, while a wrong <c>false</c> merely wastes data. Every row created by the
    /// migration is <c>false</c>, because nobody recorded whether those historical edits were
    /// cosmetic and guessing would be a fabrication. §10.1.
    /// </para>
    /// </summary>
    public bool PsychometricContinuity { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Set when a later version replaces this one. The row itself is never edited.</summary>
    public DateTime? RetiredAtUtc { get; set; }

    /// <summary>
    /// The per-language renderings. Today these are <c>Question</c> rows, which is precisely what an
    /// item localization is: stem text and choice text in one language, owning no key.
    /// </summary>
    public ICollection<Question> Localizations { get; set; } = new List<Question>();
}
