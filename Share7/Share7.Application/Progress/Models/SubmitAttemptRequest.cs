using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Progress.Models;

/// <summary>
/// One answer on the wire.
/// <para>
/// **Facts and conditions only.** The client says which choice was picked and what the conditions
/// were; the server grades, derives the attempt ordinal, resolves the evidence contract and decides
/// what any of it is worth. There is no field here in which a score, a strength or a mastery claim
/// could be asserted — the same defence that already stops a client asserting a percentage.
/// </para>
/// </summary>
public class SubmittedAnswer
{
    [Required]
    public Guid QuestionId { get; set; }

    /// <summary>The choice picked, or null when the question was never reached.</summary>
    public Guid? ChoiceId { get; set; }

    /// <summary>
    /// How long the learner had this question in front of them, in milliseconds.
    /// <para>
    /// **Client-measured because the client is the only party that can measure it** — the server
    /// sees one request carrying a whole run. Treated as a condition rather than as an input to
    /// anything the client benefits from: it is bounded on arrival and discarded when implausible,
    /// and no reward, score or unlock reads it.
    /// </para>
    /// </summary>
    [Range(0, 30 * 60 * 1000)]
    public int? ElapsedMs { get; set; }

    /// <summary>
    /// Hints shown before the learner committed. A hinted answer is evidence of something weaker
    /// than an unhinted one, which is why it is recorded rather than ignored.
    /// </summary>
    [Range(0, 100)]
    public int HintsUsed { get; set; }

    /// <summary>The limit the game imposed on this question, or null when it was untimed.</summary>
    [Range(0, 30 * 60 * 1000)]
    public int? TimeLimitMs { get; set; }

    /// <summary>
    /// Whether the learner could re-answer <i>this question within this run</i> — a practice mode
    /// that loops until correct, say. Replaying the whole lesson later is a separate administration
    /// and is counted by the attempt ordinal instead.
    /// <para>
    /// Null means the game did not say, and the server infers it from the play context.
    /// </para>
    /// </summary>
    public bool? RetryPermitted { get; set; }
}

public class SubmitAttemptRequest
{
    [Required]
    public Guid GameId { get; set; }

    [Required]
    public Guid LessonId { get; set; }

    public List<SubmittedAnswer> Answers { get; set; } = [];

    [MaxLength(128)]
    public string? ModeKey { get; set; }

    [MaxLength(32)]
    public string? ContextKey { get; set; }

    public Guid? EventId { get; set; }

    /// <summary>
    /// Required when <see cref="ContextKey"/> is <c>assignment</c>, refused otherwise. The server
    /// checks the caller is actually on the cohort's roster; naming an assignment is a claim, not
    /// an instruction.
    /// </summary>
    public Guid? AssignmentId { get; set; }

    [MaxLength(128)]
    public string? RequestId { get; set; }
}
