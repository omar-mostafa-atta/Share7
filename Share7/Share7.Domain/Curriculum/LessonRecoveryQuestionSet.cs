using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// The recovery-question cache key for one lesson in one language. Composite key
/// (LessonId, LangId) — the mirror of <see cref="LessonQuestionSet"/> for the secondary pool.
/// <para>
/// It is a separate row from the main question set on purpose: the two pools are uploaded
/// independently, so a lesson can be on recovery version 3 while its main questions are still
/// on version 1. A client caching both compares two versions, not one.
/// </para>
/// <para>
/// A missing row means version 0 — no recovery sheet has been uploaded for that lesson in that
/// language. <see cref="Version"/> increments by 1 on every upload; the client re-downloads only
/// when its cached value differs.
/// </para>
/// </summary>
public class LessonRecoveryQuestionSet
{
    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public int Version { get; set; }
}
