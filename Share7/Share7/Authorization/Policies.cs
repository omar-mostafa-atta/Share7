namespace Share7.API.Authorization;

/// <summary>
/// Named authorization policies: what kind of work an endpoint does, as opposed to which roles
/// happen to be allowed to do it today.
/// <para>
/// A controller names the work — <c>[Authorize(Policy = Policies.ContentAuthoring)]</c> — and
/// <see cref="AuthorizationExtensions.AddShare7Authorization"/> maps each name to roles, in one
/// place. Letting a new role do some kind of work is one edit there rather than an audit of every
/// controller that does it (Roles.md §6).
/// </para>
/// <para>
/// Only the content surface and Team &amp; Access have moved to policies so far. The rest of
/// <c>/api/admin</c> still says <c>[Authorize(Roles = "Admin,SuperAdmin")]</c>, which is the same
/// thing spelled out; migrate a controller here when a role other than the admins needs it.
/// </para>
/// </summary>
public static class Policies
{
    /// <summary>
    /// What is left of the old content surface in the Admin Console: curriculum health, the question
    /// search, and the lesson reads beside the writes that cutover closed. Admins and SuperAdmins.
    /// <para>
    /// It used to mean authoring, and to include the content team. Authoring moved to the Studio at
    /// cutover (plan P6): every write it used to guard now answers <c>410 Gone</c>
    /// (<see cref="ClosedAtCutoverAttribute"/>), and the content team signs in at the Studio alone.
    /// The name is kept because it still marks the same surface, and renaming a policy touches every
    /// controller that names it without changing what any of them do.
    /// </para>
    /// </summary>
    public const string ContentAuthoring = "ContentAuthoring";

    /// <summary>
    /// Deleting a curriculum node that still has content under it (<c>?force=true</c>). Admins and
    /// SuperAdmins only.
    /// <para>
    /// Kept apart from <see cref="ContentAuthoring"/> because it is the one authoring operation
    /// that cannot be put back: it removes every lesson, question and upload beneath the node, and
    /// student progress is left keyed to ids that no longer exist. The content team can delete a
    /// node once it is empty, which covers correcting a mistake without covering wiping a subject.
    /// </para>
    /// </summary>
    public const string ContentCascadeDelete = "ContentCascadeDelete";

    /// <summary>
    /// Team &amp; Access and the audit log: managing content-team accounts once they exist — their
    /// profile, role and scope, suspending, resetting and closing them, their sessions — the staff
    /// security settings, and reading what everybody did. SuperAdmins only.
    /// </summary>
    public const string ManageStaff = "ManageStaff";

    /// <summary>
    /// Adding a content-team member, and reading the curriculum and languages their scope is chosen
    /// from. Admins and SuperAdmins: since 2026-09-26 an Admin may create an account of every role
    /// but SuperAdmin — and creating is all an Admin does to the content team. Everything after the
    /// account exists is <see cref="ManageStaff"/>.
    /// </summary>
    public const string AddTeamMembers = "AddTeamMembers";

    /// <summary>
    /// Any live Studio session, including one that must set up 2-step before anything else: the
    /// member's own account endpoints. Studio tokens only.
    /// </summary>
    public const string StudioSession = "StudioSession";

    /// <summary>A Studio session with full access — 2-step set up wherever it is required.</summary>
    public const string StudioMember = "StudioMember";

    /// <summary>Writing drafts. Every Studio role can.</summary>
    public const string StudioAuthor = "StudioAuthor";

    /// <summary>Approving or sending back other people's work. Reviewers and Leads.</summary>
    public const string StudioReviewer = "StudioReviewer";

    /// <summary>Releases, rollbacks, retiring branches, assigning work. Leads only.</summary>
    public const string StudioLead = "StudioLead";
}
