namespace Share7.Application.Curriculum.Models;

/// <summary>
/// Full question payload for a lesson. The client caches this keyed by
/// <see cref="LessonId"/> and only re-fetches when <see cref="Version"/> moves.
/// </summary>
public class LessonQuestionsDto
{
    public Guid LessonId { get; set; }
    public Guid LangId { get; set; }
    public int Version { get; set; }
    public IReadOnlyList<QuestionDto> Questions { get; set; } = [];
}

public class QuestionDto
{
    public Guid QuestionId { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>Id of the entry in <see cref="Answers"/> that is correct.</summary>
    public Guid CorrectAnswerId { get; set; }

    public IReadOnlyList<AnswerDto> Answers { get; set; } = [];
}

public class AnswerDto
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
}
