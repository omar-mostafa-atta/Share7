namespace Share7.Domain.Workspace;

/// <summary>
/// A proposed change to the curriculum, written in the Content Studio and invisible to students
/// until a release publishes it.
/// <para>
/// **One draft, one target.** A lesson's questions, a new chapter or lesson, a rename, a move, a new
/// order for a parent's children, a retirement or a restore — each is its own draft, reviewed on its
/// own, and a Lead bundles approved drafts into a release. A lesson has at most one open content
/// draft, which the team shares: two authors on one lesson edit the same draft (autosaved, with the
/// revision as an If-Match) rather than two drafts that would have to be merged later.
/// </para>
/// <para>
/// **A draft remembers the live state it started from** (<see cref="BaseJson"/>,
/// <see cref="BaseFingerprint"/>). If live content moves underneath it — another release, or an old
/// admin path during the transition — the draft is out of date: it cannot be released until its
/// author has brought it up to date, and bringing it up to date sends it back through review.
/// </para>
/// </summary>
public class Draft
{
    public Guid Id { get; set; }

    public DraftKind Kind { get; set; }

    public DraftStatus Status { get; set; } = DraftStatus.Editing;

    /// <summary>
    /// Kept in step with <see cref="Status"/>: true until the draft is released or discarded. What the
    /// "one open draft per target" unique index filters on.
    /// </summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>
    /// A sandbox draft (onboarding, trying things out): reviewed like any other, never releasable,
    /// never tied to a live lesson.
    /// </summary>
    public bool IsPractice { get; set; }

    /// <summary>
    /// The node the draft changes. For <see cref="DraftKind.NewNode"/>, the id reserved for the node
    /// it will create — so a content draft or a child's draft can name it before it exists. For
    /// <see cref="DraftKind.Reorder"/>, the parent whose children are reordered. Null for practice.
    /// </summary>
    public Guid? NodeId { get; set; }

    /// <summary>The parent a new node goes under, or the parent a moved node goes to.</summary>
    public Guid? ParentNodeId { get; set; }

    /// <summary>For <see cref="DraftKind.NewNode"/>: term, subject, chapter or lesson.</summary>
    public string? NodeKind { get; set; }

    /// <summary>
    /// The materialised path the draft sits at (the target's, or its parent's for a new node) —
    /// what scope checks and "drafts under this subject" read, with one prefix comparison.
    /// </summary>
    public string ScopePath { get; set; } = string.Empty;

    /// <summary>A short label for lists: the node's English title and what the draft does.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The proposed state, shaped by <see cref="Kind"/> (see Workspace/DraftPayloads).</summary>
    public string ProposedJson { get; set; } = "{}";

    /// <summary>The live state when the draft was started or last brought up to date.</summary>
    public string BaseJson { get; set; } = "{}";

    /// <summary>
    /// What "live has not moved" is checked against: the lesson's published set versions for a
    /// content draft, the node's revision for a structural one. Empty when nothing can go stale.
    /// </summary>
    public string BaseFingerprint { get; set; } = string.Empty;

    /// <summary>Space-separated language ids the draft changes, for reviewers' language scope.</summary>
    public string LanguagesTouched { get; set; } = string.Empty;

    /// <summary>Goes up by one on every save. Autosave sends the revision it read; a moved one is refused.</summary>
    public int Revision { get; set; } = 1;

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Guid UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }

    public Guid? ReleaseId { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }

    public Guid? DiscardedByUserId { get; set; }
    public DateTime? DiscardedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<DraftContributor> Contributors { get; set; } = new List<DraftContributor>();
}

public enum DraftKind
{
    /// <summary>A lesson's questions, every pool and language.</summary>
    LessonContent = 0,

    /// <summary>A new term, subject, chapter or lesson — a new lesson may carry its questions too.</summary>
    NewNode = 1,

    Rename = 2,
    Move = 3,

    /// <summary>New order for a parent's children. <see cref="Draft.NodeId"/> is the parent.</summary>
    Reorder = 4,

    Retire = 5,
    Restore = 6,

    /// <summary>
    /// When second-chance questions are offered under one node, and how many. Changes what every
    /// child in scope is shown, so it is reviewed and released like a lesson's questions rather
    /// than set (decided 24 Sep 2026). <see cref="Draft.NodeId"/> is the node the rule is written at.
    /// </summary>
    RecoveryRule = 7
}

public enum DraftStatus
{
    Editing = 0,
    InReview = 1,

    /// <summary>A reviewer sent it back with comments. Editing again moves it to <see cref="Editing"/>.</summary>
    ChangesRequested = 2,

    /// <summary>A second person approved exactly this revision. Any edit un-approves it.</summary>
    Approved = 3,

    Released = 4,
    Discarded = 5
}

/// <summary>
/// Everyone who has changed a draft. **Nobody on this list may approve it** — "a second person
/// approves" means somebody who did not write any of it, on a draft several people can edit.
/// </summary>
public class DraftContributor
{
    public Guid DraftId { get; set; }
    public Guid UserId { get; set; }
    public DateTime FirstEditAtUtc { get; set; }
    public DateTime LastEditAtUtc { get; set; }
}

/// <summary>A comment on a draft, optionally pinned to a question, a language or a field.</summary>
public class DraftComment
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }

    /// <summary>A reply's thread. Null for a top-level comment.</summary>
    public Guid? ParentCommentId { get; set; }

    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;

    /// <summary>Where it is pinned: <c>{ itemId, role, order, langId, field }</c>, each optional. Null: the draft as a whole.</summary>
    public string? AnchorJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? EditedAtUtc { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
}

/// <summary>A reviewer's verdict on one revision of a draft. Kept forever, including superseded ones.</summary>
public class ReviewDecision
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public ReviewVerdict Verdict { get; set; }
    public string? Note { get; set; }

    /// <summary>The draft revision the verdict was given on. An approval of an older revision is void.</summary>
    public int DraftRevision { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public enum ReviewVerdict
{
    Approved = 0,
    ChangesRequested = 1
}

/// <summary>Who has a draft open right now — "Mona is editing". A heartbeat, not a lock.</summary>
public class DraftPresence
{
    public Guid DraftId { get; set; }
    public Guid UserId { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}
