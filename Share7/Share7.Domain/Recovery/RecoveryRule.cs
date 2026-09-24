namespace Share7.Domain.Recovery;

/// <summary>
/// When a child is offered second-chance questions, and how many of them.
/// <para>
/// **A rule is written at a place in the curriculum, and the most specific one wins.** A rule on a
/// grade covers every subject under it; a rule on a subject overrides the grade for that subject
/// alone; a rule on a lesson overrides both for that lesson. Nothing is merged — the winning rule
/// is used whole, because a rule half-inherited from two places is a rule nobody can predict.
/// </para>
/// <para>
/// **Every rule is released, never set.** It changes what every child in its scope is shown, which
/// puts it on the same footing as a lesson's questions: it is proposed in a draft, approved by
/// somebody other than its author, and goes out in a release with everything else (decided
/// 24 Sep 2026). <see cref="ReleaseId"/> names the release that wrote this version.
/// </para>
/// <para>
/// **The game opts in.** Nothing here changes what the game does until the game asks for the rule
/// and acts on it. Until then these rows are an authored intention that can be read, reviewed and
/// released without touching a single running client.
/// </para>
/// </summary>
public class RecoveryRule
{
    public Guid Id { get; set; }

    /// <summary>The node the rule is written at: a grade, a subject, or a lesson.</summary>
    public Guid NodeId { get; set; }

    /// <summary>
    /// The node's materialised path, copied at release. "Which rule covers this lesson" is then one
    /// indexed prefix comparison and a longest-match, with no tree walk at serve time.
    /// </summary>
    public string ScopePath { get; set; } = string.Empty;

    /// <summary>Denormalised from the node, so the Studio can group a list without a join.</summary>
    public string NodeKind { get; set; } = string.Empty;

    /// <summary>
    /// How many questions a child has to get wrong, in the main pool of one lesson, before the
    /// second-chance pool opens. One means the first mistake opens it.
    /// </summary>
    public int AfterWrongAnswers { get; set; } = RecoveryDefaults.AfterWrongAnswers;

    /// <summary>
    /// How many second-chance questions to serve once it opens. Capped by how many the lesson
    /// actually has: a rule asking for more than exist serves what exists rather than failing.
    /// </summary>
    public int QuestionsToServe { get; set; } = RecoveryDefaults.QuestionsToServe;

    /// <summary>
    /// Whether a child can be shown the same second-chance question twice in one sitting. Off by
    /// default — being asked the same question again is what a child reads as "the game is broken".
    /// </summary>
    public bool AllowRepeats { get; set; }

    /// <summary>
    /// The skill this rule is about, once a subject is mapped to real targets. **Null today and for
    /// the whole of the pilot** (decided 24 Sep 2026: simple now, per-skill later). The column
    /// exists so that adding per-skill recovery is a new row and a new branch, not a migration on a
    /// table the game is already reading.
    /// </summary>
    public Guid? TargetId { get; set; }

    /// <summary>
    /// False once a later release replaces this rule at the same node. Rows are never deleted: a
    /// child's sitting last week was governed by one of these, and "why did it do that" has to
    /// stay answerable.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The release that wrote it. Null only for rows a migration or a seed created.</summary>
    public Guid? ReleaseId { get; set; }

    public Guid? WrittenByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Set when a later release supersedes it. The row stays readable for ever.</summary>
    public DateTime? SupersededAtUtc { get; set; }
}

/// <summary>
/// What the game does where nobody has written a rule.
/// <para>
/// These are not a silent fallback: the Studio shows them on every board that has no rule of its
/// own, labelled as the platform's own, so a team can see what is happening without a rule as
/// clearly as they can see what one would change.
/// </para>
/// </summary>
public static class RecoveryDefaults
{
    public const int AfterWrongAnswers = 2;
    public const int QuestionsToServe = 3;
    public const bool AllowRepeats = false;

    /// <summary>Bounds the Studio enforces and the game can rely on. A rule outside these is refused.</summary>
    public const int MinAfterWrongAnswers = 1;
    public const int MaxAfterWrongAnswers = 20;
    public const int MinQuestionsToServe = 1;
    public const int MaxQuestionsToServe = 20;
}
