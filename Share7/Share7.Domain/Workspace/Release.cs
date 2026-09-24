namespace Share7.Domain.Workspace;

/// <summary>
/// A bundle of approved drafts published together by a Lead — **the only way anything written in
/// the Studio reaches students.**
/// <para>
/// Publishing is one database transaction: the game sees all of a release or none of it. What each
/// change replaced is captured on its <see cref="ReleaseEntry"/> as it is applied, which is what
/// makes a rollback possible — a rollback is itself a release, putting back the "before" of a chosen
/// release. Versions keep going up; nothing is rewound.
/// </para>
/// </summary>
public class Release
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public ReleaseStatus Status { get; set; } = ReleaseStatus.Building;

    /// <summary>Set for a rollback: the release whose changes it puts back.</summary>
    public Guid? RollbackOfReleaseId { get; set; }

    /// <summary>Why a rollback was needed. Required for one.</summary>
    public string? Reason { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When a scheduled release publishes itself (the start of term, say).</summary>
    public DateTime? ScheduledForUtc { get; set; }
    public Guid? ScheduledByUserId { get; set; }

    public DateTime? PublishedAtUtc { get; set; }
    public Guid? PublishedByUserId { get; set; }

    /// <summary>Why the last attempt to publish did not go through. Nothing it touched was changed.</summary>
    public string? FailureMessage { get; set; }
    public string? FailureDetailsJson { get; set; }

    /// <summary>The impact report, as computed when it was published.</summary>
    public string? ImpactJson { get; set; }

    /// <summary>Set once another release has rolled this one back.</summary>
    public Guid? RolledBackByReleaseId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<ReleaseEntry> Entries { get; set; } = new List<ReleaseEntry>();
}

public enum ReleaseStatus
{
    /// <summary>A Lead is putting it together; drafts can be added and removed.</summary>
    Building = 0,

    /// <summary>Waiting for <see cref="Release.ScheduledForUtc"/>.</summary>
    Scheduled = 1,

    /// <summary>Being applied right now. A transient state the scheduler claims.</summary>
    Publishing = 2,

    Published = 3,

    /// <summary>The last attempt was refused; nothing changed. It can be fixed and published again.</summary>
    Failed = 4,

    Cancelled = 5
}

/// <summary>One change in a release, in the order it is applied, with the state it replaced.</summary>
public class ReleaseEntry
{
    public Guid Id { get; set; }
    public Guid ReleaseId { get; set; }

    /// <summary>The draft this entry publishes. Null for a rollback's entries, which come from another release.</summary>
    public Guid? DraftId { get; set; }

    public DraftKind Kind { get; set; }

    public Guid NodeId { get; set; }

    /// <summary>Position in the apply order: restores, new nodes (parents first), renames, moves, reorders, questions, retirements.</summary>
    public int Sequence { get; set; }

    /// <summary>The live state just before this entry was applied. What a rollback puts back.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>The live state just after — what a rollback checks has not moved since.</summary>
    public string? AfterJson { get; set; }

    /// <summary>A short account of what changed: versions moved, questions added and removed.</summary>
    public string? OutcomeJson { get; set; }
}

/// <summary>A Lead pointing a member at a piece of work.</summary>
public class WorkAssignment
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public Guid AssigneeUserId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public string? Note { get; set; }
    public DateOnly? DueOn { get; set; }
    public WorkAssignmentStatus Status { get; set; } = WorkAssignmentStatus.Open;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

public enum WorkAssignmentStatus
{
    Open = 0,
    Done = 1,
    Cancelled = 2
}

/// <summary>Something a member should know about, in their Studio inbox. No email (decided 22 Sep 2026).</summary>
public class StudioNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary><c>draft.submitted</c>, <c>draft.approved</c>, <c>draft.changes_requested</c>, <c>draft.released</c>, <c>assignment.created</c>…</summary>
    public string Kind { get; set; } = string.Empty;

    public Guid? ActorUserId { get; set; }
    public Guid? DraftId { get; set; }
    public Guid? ReleaseId { get; set; }
    public Guid? AssignmentId { get; set; }
    public Guid? NodeId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
