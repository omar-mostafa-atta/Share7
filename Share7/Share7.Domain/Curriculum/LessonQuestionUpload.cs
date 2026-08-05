namespace Share7.Domain.Curriculum;

/// <summary>
/// Audit trail: one row per successful Excel upload for a lesson. Lets an admin see who
/// published which version and when, and answers "why did the question set change".
/// </summary>
public class LessonQuestionUpload
{
    public Guid Id { get; set; }

    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    /// <summary>The version this upload produced (1 for the first upload, then 2, 3, ...).</summary>
    public int Version { get; set; }

    public string FileName { get; set; } = string.Empty;
    public int QuestionCount { get; set; }

    public Guid? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
}
