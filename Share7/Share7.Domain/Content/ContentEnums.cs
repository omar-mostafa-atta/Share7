namespace Share7.Domain.Content;

/// <summary>
/// Who owns an <see cref="ItemBank"/>, which decides how far its items may be trusted.
/// <para>
/// **This is the whole teacher-authoring safety mechanism.** A teacher may write anything and their
/// students get value from it immediately; what is bounded is the consequence, not the authoring.
/// See <c>Docs/EducationalArchitecture.md</c> §10.2.
/// </para>
/// </summary>
public enum ItemBankOwnerScope
{
    /// <summary>Share7's own content. Editorially reviewed; statistics pool globally.</summary>
    Platform = 0,

    /// <summary>Published by a curriculum authority (a ministry, an examination board).</summary>
    Authority = 1,

    /// <summary>A school or tutoring centre's own bank, governed by that org's review policy.</summary>
    Organization = 2,

    /// <summary>One teacher's personal bank. Unreviewed by default.</summary>
    User = 3
}

/// <summary>What review an item in this bank has passed before it is used.</summary>
public enum ItemBankReviewPolicy
{
    /// <summary>None. The item is usable but its evidence is capped.</summary>
    Unreviewed = 0,

    /// <summary>A human has checked the content is correct and the key is right.</summary>
    Editorial = 1,

    /// <summary>Editorial review plus statistical screening against real responses.</summary>
    EditorialAndPsychometric = 2
}

/// <summary>
/// The response shape of an item version. A string rather than an enum because the set grows with
/// authoring tools and a new kind must not require a migration of every stored row.
/// <para>
/// Only <see cref="SingleChoice"/> is implemented today and every existing question maps to it
/// cleanly. The column exists now because <c>LearnerResponse</c> is the one table that is genuinely
/// expensive to reshape after it has a billion rows — §10.5.
/// </para>
/// </summary>
public static class ItemKinds
{
    public const string SingleChoice = "single_choice";
    public const string MultiChoice = "multi_choice";
    public const string Numeric = "numeric";
    public const string ShortText = "short_text";
    public const string Ordering = "ordering";
    public const string Matching = "matching";
    public const string Constructed = "constructed";
}

/// <summary>
/// What part an item plays at one curriculum node. Replaces the parallel
/// <c>Question</c> / <c>RecoveryQuestion</c> table families with data — §10.4.
/// </summary>
public enum NodeItemRole
{
    /// <summary>The lesson's main pool. Today's <c>Question</c>.</summary>
    Core = 0,

    /// <summary>Extra rehearsal, not used to judge.</summary>
    Practice = 1,

    /// <summary>The second-chance pool. Today's <c>RecoveryQuestion</c>.</summary>
    Recovery = 2,

    /// <summary>Administered to find out where a learner is, not to teach.</summary>
    Diagnostic = 3,

    /// <summary>
    /// Administered across many contexts on purpose, so measurements taken apart can be put on one
    /// scale later. See <see cref="Item.IsAnchor"/>.
    /// </summary>
    Anchor = 4
}
