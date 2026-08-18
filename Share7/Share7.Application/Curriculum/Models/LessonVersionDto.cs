namespace Share7.Application.Curriculum.Models;

/// <summary>
/// Cheap cache-validation response — the client compares this against the version it
/// already has on device before deciding to download the full question set.
/// </summary>
public class LessonVersionDto
{
    public Guid LessonId { get; set; }

    /// <summary>Which language this version belongs to — the caller's content language.</summary>
    public Guid LangId { get; set; }

    /// <summary>0 means no question sheet has been uploaded for this lesson in this language yet.</summary>
    public int Version { get; set; }

    public int QuestionCount { get; set; }
}
