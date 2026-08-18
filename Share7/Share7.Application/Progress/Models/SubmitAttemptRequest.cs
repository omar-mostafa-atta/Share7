using System.ComponentModel.DataAnnotations;

namespace Share7.Application.Progress.Models;

/// <summary>
/// One question and the choice the student actually picked.
/// <para>
/// <see cref="ChoiceId"/> is what they *chose*, right or wrong — the client makes no claim about
/// correctness and the server does not accept one. Null means the question was shown and skipped.
/// </para>
/// </summary>
public class SubmittedAnswer
{
    [Required]
    public Guid QuestionId { get; set; }

    /// <summary>The chosen answer, or null when it was left unanswered. Either way the server grades it.</summary>
    public Guid? ChoiceId { get; set; }
}

/// <summary>
/// What the game posts when a student finishes a run of a lesson.
/// <para>
/// **The client reports what was picked; the server decides what was right.** It sends one entry per
/// question with the chosen choice id, and grading happens here against
/// <c>Question.CorrectChoiceId</c>. Nothing in this payload asserts a score, so there is nothing for
/// a modified client to inflate — which is the point of the shape.
/// </para>
/// <para>
/// Questions belonging to the lesson but absent from <see cref="Answers"/> are recorded as wrong,
/// exactly like ones answered wrongly: a run shows every question in the lesson, so not reaching one
/// is not the same as getting it right.
/// </para>
/// </summary>
public class SubmitAttemptRequest
{
    [Required]
    public Guid GameId { get; set; }

    [Required]
    public Guid LessonId { get; set; }

    /// <summary>
    /// One entry per question the student answered or skipped. A question may appear only once —
    /// two answers for the same question is a client bug with no defensible resolution, so it is
    /// refused rather than silently resolved.
    /// </summary>
    public List<SubmittedAnswer> Answers { get; set; } = [];

    /// <summary>
    /// Optional client-generated id identifying **this submission**, so a retry after a lost
    /// response is recognised as the same attempt rather than a new one.
    /// <para>
    /// Only rewards use it, and only rules that pay on every attempt are affected: without it a
    /// resubmitted run is indistinguishable from genuinely replaying the lesson, and is paid twice.
    /// Progress itself is unaffected either way — the score is recomputed and overwritten, not
    /// accumulated.
    /// </para>
    /// <para>
    /// Generate one id per run and **reuse it for every retry of that run**, exactly as
    /// <c>requestId</c> works for a purchase.
    /// </para>
    /// </summary>
    [MaxLength(128)]
    public string? RequestId { get; set; }
}
