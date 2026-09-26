namespace Share7.Domain.Audit;

/// <summary>
/// One thing somebody did to the platform, written in the same transaction as the change itself.
/// **Append-only**: a database trigger refuses every UPDATE and DELETE, so a row that exists is a
/// row that happened.
/// <para>
/// This is the "accounting" of authentication, authorization and accounting. Before it, creating
/// or deleting curriculum, publishing questions and creating or deleting accounts left no trace of
/// who did it — the only attributable record on the platform was the uploader on a question set.
/// </para>
/// <para>
/// <b>Ids, never personal details.</b> The actor and the target are recorded by id, and
/// <see cref="Summary"/> and <see cref="DataJson"/> carry no names, usernames or emails: a viewer
/// resolves a display name at read time. That is what lets these rows outlive the accounts they
/// mention without anything to scrub (<c>UserOwnedData.RetainedOnDeletion</c>) — once an account is
/// erased, its id resolves to nothing.
/// </para>
/// </summary>
public class AuditEvent
{
    public Guid Id { get; set; }

    /// <summary>Database-assigned, strictly increasing. The order events happened in.</summary>
    public long Sequence { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Who acted. Null for the platform itself — a startup seed, a scheduled job.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// The roles the actor held at that moment, comma-separated. Recorded rather than looked up
    /// later, because roles change and "was an admin when they did this" is the question audits ask.
    /// </summary>
    public string ActorRoles { get; set; } = string.Empty;

    /// <summary>What happened — one of <see cref="AuditActions"/>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Which part of the platform — one of <see cref="AuditAreas"/>. What the viewer filters on.</summary>
    public string Area { get; set; } = string.Empty;

    /// <summary>What kind of thing was acted on — <c>lesson</c>, <c>account</c> — or null.</summary>
    public string? TargetType { get; set; }

    /// <summary>Its id, as text so a non-Guid key (a board key, a SKU) fits too.</summary>
    public string? TargetId { get; set; }

    /// <summary>One plain sentence: "Deleted a lesson and everything under it." No personal details.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The specifics as JSON — versions, counts, the language, whether it was forced. Ids only.</summary>
    public string? DataJson { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>The request's trace id, which ties this row to the server log for the same call.</summary>
    public string? CorrelationId { get; set; }
}

/// <summary>Stable names for <see cref="AuditEvent.Area"/>.</summary>
public static class AuditAreas
{
    public const string Curriculum = "curriculum";
    public const string Questions = "questions";
    public const string Accounts = "accounts";
    public const string Security = "security";

    /// <summary>Team &amp; Access: content-team members and the staff security settings.</summary>
    public const string Team = "team";

    /// <summary>A member's own Studio account: activation, password, 2-step.</summary>
    public const string Studio = "studio";

    /// <summary>Drafts, reviews and releases in the Content Studio.</summary>
    public const string Workspace = "workspace";
}

/// <summary>
/// Stable names for <see cref="AuditEvent.Action"/>: <c>area.thing.verb</c>, past tense.
/// <para>
/// Stored as text and never renamed — the audit viewer, exports and anybody's saved filter read
/// these strings, and an old row keeps the name it was written with. Add; do not rename.
/// </para>
/// </summary>
public static class AuditActions
{
    public const string CurriculumCreated = "curriculum.created";
    public const string CurriculumUpdated = "curriculum.updated";
    public const string CurriculumNodeCreated = "curriculum.node.created";

    /// <summary>Written by hard deletes before the engine rebuild. Nothing writes it now: deleting retires.</summary>
    public const string CurriculumNodeDeleted = "curriculum.node.deleted";
    public const string CurriculumNodeRenamed = "curriculum.node.renamed";
    public const string CurriculumNodeMoved = "curriculum.node.moved";
    public const string CurriculumNodesReordered = "curriculum.nodes.reordered";
    public const string CurriculumNodeRetired = "curriculum.node.retired";
    public const string CurriculumNodeRestored = "curriculum.node.restored";

    public const string QuestionsPublished = "questions.published";

    public const string AccountCreated = "accounts.account.created";
    public const string AccountDeleted = "accounts.account.deleted";

    public const string SuperAdminBootstrapped = "security.superadmin.bootstrapped";

    // ---- Team & Access (a SuperAdmin acting on a member) ------------------------------------
    public const string TeamMemberCreated = "team.member.created";
    public const string TeamMemberAdopted = "team.member.adopted";
    public const string TeamMemberProfileUpdated = "team.member.profile_updated";
    public const string TeamMemberAccessChanged = "team.member.access_changed";
    public const string TeamMemberNotesUpdated = "team.member.notes_updated";
    public const string TeamMemberSuspended = "team.member.suspended";
    public const string TeamMemberReactivated = "team.member.reactivated";
    public const string TeamMemberDeactivated = "team.member.deactivated";
    public const string TeamMemberAccessReset = "team.member.access_reset";
    public const string TeamMemberPasswordSet = "team.member.password_set";
    public const string TeamSetupLinkIssued = "team.setup_link.issued";
    public const string TeamSetupLinkRevoked = "team.setup_link.revoked";
    public const string TeamMemberSignedOutEverywhere = "team.member.signed_out_everywhere";
    public const string TeamSessionRevoked = "team.session.revoked";
    public const string TeamSecurityUpdated = "team.security.updated";

    // ---- the member's own Studio account ----------------------------------------------------
    public const string StudioAccountActivated = "studio.account.activated";
    public const string StudioPasswordSet = "studio.password.set";
    public const string StudioPasswordChanged = "studio.password.changed";
    public const string StudioTwoStepEnabled = "studio.two_step.enabled";
    public const string StudioTwoStepDisabled = "studio.two_step.disabled";
    public const string StudioRecoveryCodesRegenerated = "studio.recovery_codes.regenerated";
    public const string StudioRecoveryCodeUsed = "studio.recovery_code.used";
    public const string StudioSessionRevoked = "studio.session.revoked";
    public const string StudioRefreshTokenReused = "studio.session.refresh_token_reused";

    // ---- the Content Studio's workspace ------------------------------------------------------
    public const string DraftCreated = "workspace.draft.created";
    public const string DraftSubmitted = "workspace.draft.submitted";
    public const string DraftWithdrawn = "workspace.draft.withdrawn";
    public const string DraftDiscarded = "workspace.draft.discarded";
    public const string DraftRebased = "workspace.draft.brought_up_to_date";
    public const string DraftApproved = "workspace.draft.approved";

    // A Lead approving a draft they wrote part of. Allowed since 25 Sep 2026 — a Lead does not need a
    // second person — and kept apart from ordinary approvals so every one of them can be found.
    public const string DraftSelfApproved = "workspace.draft.self_approved";
    public const string DraftChangesRequested = "workspace.draft.changes_requested";
    public const string DraftImported = "workspace.draft.imported";
    public const string ReleaseCreated = "workspace.release.created";
    public const string ReleaseScheduled = "workspace.release.scheduled";
    public const string ReleaseCancelled = "workspace.release.cancelled";
    public const string ReleasePublished = "workspace.release.published";
    public const string ReleaseFailed = "workspace.release.failed";
    public const string ReleaseRolledBack = "workspace.release.rolled_back";
    public const string AssignmentCreated = "workspace.assignment.created";
    public const string AssignmentClosed = "workspace.assignment.closed";

    // Skills and measurement (plan Phase 5). These are not released: they change what a child's
    // answers MEAN, never what the child is asked, so they take effect at once and leave a row.
    public const string FrameworkCreated = "workspace.framework.created";
    public const string SkillsImported = "workspace.skills.imported";
    public const string SkillEdited = "workspace.skill.edited";
    public const string SkillReviewed = "workspace.skill.reviewed";
    public const string SkillPromoted = "workspace.skill.promoted";
    public const string QuestionMapped = "workspace.question.mapped";
    public const string ItemAnchored = "workspace.item.anchored";
    public const string ObservationsExcluded = "workspace.observations.excluded";
    public const string BenchmarkBuilt = "workspace.benchmark.built";
    public const string BlueprintPublished = "workspace.blueprint.published";
}
