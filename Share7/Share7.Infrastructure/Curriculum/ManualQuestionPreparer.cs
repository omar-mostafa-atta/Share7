using Share7.Application.Curriculum.Models;

namespace Share7.Infrastructure.Curriculum;

/// <summary>One answer of a question that is already published.</summary>
internal sealed record ExistingChoice(Guid Id, string Text);

/// <summary>
/// A published question read back in the shape an append needs: the text, which choice counts as
/// right, and all three answers. Correctness is carried as the id rather than assumed to be the
/// first choice — the id is what the schema treats as authoritative, and an append that guessed
/// would quietly change which answer is right.
/// </summary>
internal sealed record ExistingQuestion(string Text, Guid CorrectChoiceId, IReadOnlyList<ExistingChoice> Choices);

/// <summary>
/// Turns a hand-typed request into the list of questions to publish, or into the reasons it cannot
/// be published.
/// <para>
/// Pool-agnostic on purpose: the main and recovery sets store their questions in different tables
/// but accept identical content, so this takes a delegate for reading the current set rather than
/// a <c>DbContext</c>. Both services get the same behaviour from one implementation instead of two
/// that would drift.
/// </para>
/// </summary>
internal static class ManualQuestionPreparer
{
    /// <summary>Longest excerpt of a question quoted back in an error message.</summary>
    private const int ExcerptLength = 60;

    internal sealed record Prepared(
        IReadOnlyList<PublishableQuestion> Rows,
        IReadOnlyList<QuestionImportError> Errors);

    /// <summary>
    /// Validates every typed question and, for an append, folds in the set already published.
    /// <para>
    /// All-or-nothing, matching the sheet path: one bad question rejects the whole request and
    /// nothing is written. Every problem across every question is reported at once, so an admin
    /// fixes the form in one pass rather than discovering faults one submission at a time.
    /// </para>
    /// </summary>
    /// <param name="loadActive">
    /// Reads the currently published set. Called **only** for an append — a replace discards it, so
    /// making this lazy keeps the common case down to no extra query.
    /// </param>
    internal static async Task<Prepared> PrepareAsync(
        ManualQuestionSetRequest request,
        Func<Task<IReadOnlyList<ExistingQuestion>>> loadActive)
    {
        var errors = new List<QuestionImportError>();

        if (request.Mode is not { } mode)
        {
            errors.Add(new QuestionImportError
            {
                Message = "mode is required — APPEND adds these to the published set, REPLACE publishes them instead of it."
            });
            return new Prepared([], errors);
        }

        var typed = request.Questions ?? [];

        // Mirrors the sheet path's refusal of an empty workbook. An empty append would be a
        // harmless no-op, but it is far more likely a mis-submitted form than an intention, and a
        // silent success there teaches an admin that the button does nothing.
        if (typed.Count == 0)
        {
            errors.Add(new QuestionImportError { Message = "The request contains no questions." });
            return new Prepared([], errors);
        }

        var entered = new List<PublishableQuestion>(typed.Count);

        for (var index = 0; index < typed.Count; index++)
        {
            var input = typed[index];
            var position = index + 1;

            // Trimmed before validation, exactly as sheet cells are: an admin who leaves a trailing
            // space should not get a different answer from one who does not.
            var text = (input.Text ?? string.Empty).Trim();
            var correct = (input.CorrectChoice ?? string.Empty).Trim();
            var wrong1 = (input.WrongChoice1 ?? string.Empty).Trim();
            var wrong2 = (input.WrongChoice2 ?? string.Empty).Trim();

            var problems = QuestionContentRules.Validate(
                text, correct, wrong1, wrong2,
                "The question text",
                "The correct choice",
                "Wrong choice 1",
                "Wrong choice 2");

            if (problems.Count > 0)
            {
                errors.AddRange(problems.Select(message => new QuestionImportError
                {
                    Row = position,
                    Message = message
                }));
                continue;
            }

            entered.Add(new PublishableQuestion(position, text, correct, wrong1, wrong2));
        }

        if (errors.Count > 0)
            return new Prepared([], errors);

        if (mode == ManualQuestionMode.Replace)
            return new Prepared(Renumber(entered), errors);

        var carried = CarryForward(await loadActive(), errors);
        if (errors.Count > 0)
            return new Prepared([], errors);

        var combined = carried.Concat(entered).ToList();

        if (combined.Count > QuestionContentRules.MaxQuestionsPerSet)
        {
            errors.Add(new QuestionImportError
            {
                Message = $"Appending {entered.Count} would take this lesson to {combined.Count} questions, "
                          + $"above the {QuestionContentRules.MaxQuestionsPerSet} limit."
            });
            return new Prepared([], errors);
        }

        return new Prepared(Renumber(combined), errors);
    }

    /// <summary>
    /// Flattens the published set back into publishable questions so an append can republish it.
    /// <para>
    /// A question that does not resolve to one correct answer and two wrong ones is **refused
    /// rather than repaired**. Anything the importer wrote has exactly that shape, so hitting this
    /// means the row was written by something else — and guessing which answer was meant to be
    /// right would silently re-key a question students are already being graded on.
    /// </para>
    /// </summary>
    private static List<PublishableQuestion> CarryForward(
        IReadOnlyList<ExistingQuestion> existing,
        List<QuestionImportError> errors)
    {
        var carried = new List<PublishableQuestion>(existing.Count);

        foreach (var question in existing)
        {
            var correct = question.Choices.FirstOrDefault(c => c.Id == question.CorrectChoiceId);
            var wrong = question.Choices.Where(c => c.Id != question.CorrectChoiceId).ToList();

            if (correct is null || wrong.Count != 2)
            {
                errors.Add(new QuestionImportError
                {
                    Message = $"The published question \"{Excerpt(question.Text)}\" does not have one correct answer "
                              + "and two wrong ones, so the current set cannot be carried forward. "
                              + "Publish a corrected set with REPLACE instead."
                });
                continue;
            }

            // Renumbered by the caller once the two lists are joined, so the placeholder here is
            // never the value that gets stored.
            carried.Add(new PublishableQuestion(0, question.Text, correct.Text, wrong[0].Text, wrong[1].Text));
        }

        return carried;
    }

    /// <summary>
    /// Stamps positions 1..N over the final list. Position is what the read path orders by, so it
    /// has to describe the published set rather than where each question came from.
    /// </summary>
    private static List<PublishableQuestion> Renumber(IReadOnlyList<PublishableQuestion> rows) =>
        rows.Select((row, index) => row with { RowNumber = index + 1 }).ToList();

    private static string Excerpt(string text) =>
        text.Length <= ExcerptLength ? text : text[..ExcerptLength] + "…";
}
