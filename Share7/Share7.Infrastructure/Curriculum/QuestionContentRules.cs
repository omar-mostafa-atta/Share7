namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// What makes one question valid, independent of where it came from.
/// <para>
/// Extracted from <see cref="QuestionSheetParser"/> when hand entry was added. A question typed
/// into the admin console and a question read out of a spreadsheet must be held to the **same**
/// rules — otherwise the sheet path and the manual path disagree about what content is
/// acceptable, and the same question is publishable through one and refused through the other.
/// </para>
/// <para>
/// The caller supplies the field labels because only the caller knows what the admin is looking
/// at: "Column 2 (correct answer)" means something in front of a spreadsheet and nothing in front
/// of a form. One set of rules, two vocabularies.
/// </para>
/// </summary>
internal static class QuestionContentRules
{
    /// <summary>Guard against a runaway set being published by accident.</summary>
    internal const int MaxQuestionsPerSet = 5000;

    internal const int MaxQuestionLength = 1000;
    internal const int MaxChoiceLength = 500;

    /// <summary>
    /// Validates one question's four fields and returns a message per problem — empty when the
    /// question is publishable. Every problem is reported rather than only the first, so an admin
    /// fixes a row once instead of resubmitting to discover the next fault.
    /// </summary>
    internal static List<string> Validate(
        string questionText,
        string correct,
        string wrong1,
        string wrong2,
        string questionLabel,
        string correctLabel,
        string wrong1Label,
        string wrong2Label)
    {
        var errors = new List<string>();

        if (questionText.Length == 0)
            errors.Add($"{questionLabel} is empty.");
        else if (questionText.Length > MaxQuestionLength)
            errors.Add($"{questionLabel} is {questionText.Length} characters, above the {MaxQuestionLength} limit.");

        var choices = new[]
        {
            (Label: correctLabel, Value: correct),
            (Label: wrong1Label, Value: wrong1),
            (Label: wrong2Label, Value: wrong2)
        };

        foreach (var (label, value) in choices)
        {
            if (value.Length == 0)
                errors.Add($"{label} is empty.");
            else if (value.Length > MaxChoiceLength)
                errors.Add($"{label} is {value.Length} characters, above the {MaxChoiceLength} limit.");
        }

        // Two identical doors where one counts as wrong would be unanswerable.
        // Compared case-SENSITIVELY on purpose: capitalisation is often the thing being tested
        // ("Fe" vs "FE" vs "fe" for iron's chemical symbol is a real question), so folding case
        // here would reject valid content.
        var present = choices.Where(c => c.Value.Length > 0).ToList();
        var distinct = present.Select(c => c.Value).Distinct(StringComparer.Ordinal).Count();

        if (distinct != present.Count)
            errors.Add("The three answers must be different from each other.");

        return errors;
    }
}
