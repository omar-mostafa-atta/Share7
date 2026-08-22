namespace Share7.Application.Curriculum.Models;

/// <summary>
/// What a hand-typed publish does to the set already there.
/// <para>
/// Deliberately has no default. Publishing a question set is destructive in one of its two
/// meanings and harmless in the other, and which one the caller wanted is not a thing to guess at
/// — an omitted mode is refused rather than assumed. The console always sends it explicitly.
/// </para>
/// </summary>
public enum ManualQuestionMode
{
    /// <summary>
    /// Keep the questions already published and add these after them. What "add a question by
    /// hand" means, and the reason this exists at all — the Excel path can only ever replace.
    /// <para>
    /// Still produces a **new version**, like every other publish: the carried-forward questions
    /// are re-created alongside the new ones and the previous rows are retired. The set is
    /// immutable by design, so appending is a republish of a longer list rather than an insert.
    /// </para>
    /// </summary>
    Append,

    /// <summary>
    /// Discard the current set and publish these instead — the same thing an Excel upload does,
    /// typed rather than uploaded. This is how a question gets edited or removed: load the set,
    /// change it, publish it back.
    /// </summary>
    Replace
}

/// <summary>
/// One question as an admin types it: the text, and three answers of which the first is correct.
/// <para>
/// Correctness is positional rather than a flag per choice, matching the spreadsheet contract
/// (column 2 is the correct answer) and the storage model, where correctness lives on the question
/// rather than the choice. An <c>isCorrect</c> boolean per answer would make "none of them" and
/// "two of them" representable, and both are unanswerable in a three-door game.
/// </para>
/// <para>
/// **Deliberately carries no <c>[Required]</c> attributes.** Model validation runs before the
/// action does, so an annotation here would refuse the request in the framework's own error shape
/// — an object keyed by <c>Questions[1].Text</c> — and the caller would get something structurally
/// different from what a rejected spreadsheet returns. It would also stop at the first fault,
/// where the real validator reports every problem in every question at once. Emptiness is checked
/// alongside the rules an attribute cannot express, so that one validator owns the whole answer.
/// </para>
/// </summary>
public class ManualQuestionInput
{
    /// <summary>The question as the student reads it.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The right answer. Stored first, and pointed at by the question's correct-choice id.</summary>
    public string CorrectChoice { get; set; } = string.Empty;

    /// <summary>A distractor. Must differ from the other two — case-sensitively.</summary>
    public string WrongChoice1 { get; set; } = string.Empty;

    /// <summary>The second distractor. Three answers, one per lane in the runner game.</summary>
    public string WrongChoice2 { get; set; } = string.Empty;
}

/// <summary>
/// A hand-typed publish for one lesson in one language — the manual counterpart of uploading a
/// sheet, landing in the same tables, under the same validation, producing the same next version.
/// </summary>
public class ManualQuestionSetRequest
{
    /// <summary>
    /// Whether these questions join the current set or replace it. Required — see
    /// <see cref="ManualQuestionMode"/> for why there is no default, and
    /// <see cref="ManualQuestionInput"/> for why that is enforced in the service rather than by an
    /// attribute.
    /// </summary>
    public ManualQuestionMode? Mode { get; set; }

    /// <summary>
    /// The questions being published. Validated all-or-nothing: one bad entry rejects the whole
    /// request and leaves the current version untouched, exactly as a bad sheet does.
    /// <para>
    /// May be empty **only** under <see cref="ManualQuestionMode.Append"/>, where it is a no-op
    /// rather than an error. An empty <see cref="ManualQuestionMode.Replace"/> would publish a
    /// lesson with no questions, which reads as "unplayable in this language" — too destructive to
    /// be expressible by accident.
    /// </para>
    /// </summary>
    public List<ManualQuestionInput> Questions { get; set; } = [];
}
