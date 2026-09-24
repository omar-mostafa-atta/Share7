namespace Share7.Domain.Staff;

/// <summary>
/// Where a content-team account is in its life. Only <see cref="Active"/> may sign in to the Studio.
/// <para>
/// <c>Invited → Active ⇄ Suspended → Deactivated</c>. Deactivation is final: the account is never
/// hard-deleted, so the person's name stays on everything they authored, reviewed and published.
/// </para>
/// </summary>
public enum StaffStatus
{
    /// <summary>Created by a SuperAdmin; the setup link has not been used yet. No password exists.</summary>
    Invited = 0,

    Active = 1,

    /// <summary>Signed out everywhere and unable to sign in. Reversible.</summary>
    Suspended = 2,

    /// <summary>Can never sign in again. Not reversible.</summary>
    Deactivated = 3
}

/// <summary>
/// What a member may do in the Studio. <b>Ordered</b>: each role can do everything the one before it
/// can — a Reviewer also writes drafts, a Lead also reviews. Compare with <c>&gt;=</c>.
/// </summary>
public enum StudioRole
{
    /// <summary>Writes drafts inside their scope and submits them for review.</summary>
    Author = 0,

    /// <summary>Also approves or requests changes on other people's work.</summary>
    Reviewer = 1,

    /// <summary>Also publishes releases, rolls them back, retires branches and assigns work.</summary>
    Lead = 2
}

/// <summary>
/// A content-team member: the Studio's view of an Identity account that holds the
/// <c>ContentTeam</c> role.
/// <para>
/// <b>One row per account, keyed by the user id.</b> The Identity row owns the credentials
/// (password hash, lockout, 2-step key); this row owns everything the content team and the
/// SuperAdmins see about the person, and the Studio's own authorization — role and scope. Role and
/// scope are read on the server for every request rather than written into the token, so a change
/// made in Team &amp; Access applies within seconds instead of at the next sign-in.
/// </para>
/// <para>
/// Only a SuperAdmin creates one (Team &amp; Access). It is never deleted: deactivation is the end of
/// the road, and the name must stay resolvable on the audit trail and on authored content.
/// </para>
/// </summary>
public class StaffProfile
{
    public Guid UserId { get; set; }

    /// <summary>Shown on everything the person authors, reviews and publishes.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>For example "Science specialist". Optional.</summary>
    public string? JobTitle { get; set; }

    /// <summary>
    /// For contact only. <b>Never</b> used to sign in or to link a Google or Facebook identity —
    /// that is why it is not the Identity account's <c>Email</c>.
    /// </summary>
    public string? WorkEmail { get; set; }

    public StudioRole StudioRole { get; set; }

    /// <summary>
    /// True: every part of the curriculum. False: only the branches in <see cref="ScopeNodes"/>
    /// (and everything under them). Scope limits what a person may <i>change</i>, never what they
    /// may see.
    /// </summary>
    public bool AllNodes { get; set; }

    /// <summary>True: every content language. False: only <see cref="ScopeLanguages"/>.</summary>
    public bool AllLanguages { get; set; }

    /// <summary>The Studio's interface language, <c>"en"</c> or <c>"ar"</c>. The member can change it.</summary>
    public string InterfaceLanguage { get; set; } = StaffInterfaceLanguages.English;

    public StaffStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>The SuperAdmin who created the profile. Null for one created by the platform.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>When the setup link was first used. Null while <see cref="StaffStatus.Invited"/>.</summary>
    public DateTime? ActivatedAtUtc { get; set; }

    public DateTime? StatusChangedAtUtc { get; set; }
    public Guid? StatusChangedByUserId { get; set; }

    /// <summary>Why the last suspension or deactivation happened, as the SuperAdmin wrote it.</summary>
    public string? StatusReason { get; set; }

    /// <summary>Private to SuperAdmins. Never shown in the Studio.</summary>
    public string? Notes { get; set; }

    /// <summary>The last successful Studio sign-in or session refresh.</summary>
    public DateTime? LastActiveAtUtc { get; set; }

    /// <summary>
    /// The last 2-step code accepted and when, so the same six digits cannot be replayed while
    /// they are still inside their time window. Stored hashed.
    /// </summary>
    public string? LastTwoStepCodeHash { get; set; }
    public DateTime? LastTwoStepAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Concurrency token: two SuperAdmins editing the same member cannot silently overwrite each other.</summary>
    public byte[] RowVersion { get; set; } = [];

    public ICollection<StaffScopeNode> ScopeNodes { get; set; } = new List<StaffScopeNode>();
    public ICollection<StaffScopeLanguage> ScopeLanguages { get; set; } = new List<StaffScopeLanguage>();
}

/// <summary>
/// One branch of the curriculum a member may change: the node and everything under it.
/// <para>
/// <b>No foreign key to <c>CurriculumNodes</c>, deliberately.</b> Nodes are still a projection of
/// the legacy tables and the projector owns their rows; a cascade would let a projection rebuild
/// silently empty somebody's scope, and a restrict would let a scope block it. A node that no
/// longer exists simply matches nothing, and Team &amp; Access shows it as removed.
/// </para>
/// </summary>
public class StaffScopeNode
{
    public Guid UserId { get; set; }
    public Guid NodeId { get; set; }
}

/// <summary>One content language a member may write in.</summary>
public class StaffScopeLanguage
{
    public Guid UserId { get; set; }
    public Guid LanguageId { get; set; }
}

public static class StaffInterfaceLanguages
{
    public const string English = "en";
    public const string Arabic = "ar";

    public static readonly string[] All = [English, Arabic];
}
