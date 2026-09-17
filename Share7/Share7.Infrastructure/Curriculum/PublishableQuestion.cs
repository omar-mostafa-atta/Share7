namespace Share7.Infrastructure.Curriculum;

/// <summary>
/// One validated question on its way into a new version, with the source it came from already
/// forgotten.
/// <para>
/// This is the seam between "where did this question come from" and "how does a question set get
/// published". A spreadsheet row and a form submission arrive as different things and are validated
/// against different-sounding labels, but past this point they are identical — which is what lets
/// both publish paths share one writer instead of two that can drift apart.
/// </para>
/// </summary>
/// <param name="RowNumber">
/// Position in the published set, 1-based. For a sheet it is the row it came from, which is what
/// makes an import error findable in the file; for a hand-typed set it is simply the order the
/// admin entered them. Either way it is the order the questions are served in.
/// </param>
internal sealed record PublishableQuestion(
    int RowNumber,
    string QuestionText,
    string CorrectAnswer,
    string WrongAnswer1,
    string WrongAnswer2);
