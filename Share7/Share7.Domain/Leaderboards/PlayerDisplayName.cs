namespace Share7.Domain.Leaderboards;

/// <summary>
/// The name a player is shown under on a public board, and whether they are listed at all.
/// <para>
/// **This table exists because no safe name already existed.** <c>StudentProfile.FullName</c> is a
/// child's real name. Identity's <c>UserName</c> is unmoderated free text the user typed — and on
/// the external-login path it is set to their <em>email address</em>. Every field the schema had
/// before this one leaks personal data the moment it is rendered next to a rank, and the users are
/// children.
/// </para>
/// <para>
/// So a board row never reads any of them. It reads a generated handle that is derived from
/// nothing: not the name, not the email, not the grade, not the account id. Two children called
/// the same thing get different handles, and a handle tells an observer nothing about who holds it.
/// </para>
/// </summary>
public class PlayerDisplayName
{
    /// <summary>PK. One handle per account.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The public handle, e.g. <c>SwiftFalcon418</c>. Unique across the platform, so a row is
    /// unambiguous without exposing the account id it belongs to.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>
    /// Where the handle came from. Only <see cref="DisplayNameSource.Generated"/> is issued today;
    /// <see cref="DisplayNameSource.Chosen"/> exists so that letting a child pick their own handle
    /// later is a new value here rather than a new column, and it must not be issued before a
    /// moderation queue exists to review what they typed.
    /// </summary>
    public DisplayNameSource Source { get; set; }

    /// <summary>
    /// The player is not listed on public pages. They keep their rank and can still see it
    /// themselves — hiding is not forfeiting.
    /// <para>
    /// The default is configured rather than hard-coded here, because "listed by default with an
    /// opt-out" versus "unlisted until opted in" is a product and legal ruling, not an engineering
    /// one, and it has to be flippable without a migration.
    /// </para>
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Set when a guardian, rather than the player, forced the account unlisted. Kept apart from
    /// <see cref="IsHidden"/> so a child cannot undo a guardian's decision by toggling their own
    /// setting.
    /// </summary>
    public bool IsHiddenByGuardian { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}

public enum DisplayNameSource
{
    /// <summary>Issued by the platform from a curated word list. Carries no personal data.</summary>
    Generated = 0,

    /// <summary>Chosen by the player and cleared by moderation. Not issued yet.</summary>
    Chosen = 1
}
