using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// Audit trail: one row per successful recovery-question Excel upload for a lesson. Lets an admin
/// see who published which version of the secondary pool and when. Mirrors
/// <see cref="LessonQuestionUpload"/>, kept separate so the two pools' histories do not interleave.
/// </summary>
public class LessonRecoveryQuestionUpload
{
    public Guid Id { get; set; }

    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    /// <summary>Which language's recovery question set this upload published.</summary>
    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    /// <summary>The version this upload produced (1 for the first upload, then 2, 3, ...).</summary>
    public int Version { get; set; }

    public string FileName { get; set; } = string.Empty;
    public int QuestionCount { get; set; }

    public Guid? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
}
