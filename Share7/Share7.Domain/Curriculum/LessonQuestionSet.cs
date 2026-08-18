using Share7.Domain.LookUps;

namespace Share7.Domain.Curriculum;

/// <summary>
/// The question-cache key for one lesson in one language. Composite key (LessonId, LangId).
/// <para>
/// This is what <c>Lessons.QuestionsVersion</c> used to be. It moved off the lesson row when
/// the tree became language-shared: a single int cannot version two independently uploaded
/// question sets.
/// </para>
/// <para>
/// A missing row means version 0 — no sheet has been uploaded for that lesson in that
/// language, so the lesson is not playable in it. <see cref="Version"/> increments by 1 on
/// every upload; the client re-downloads only when its cached value differs.
/// </para>
/// </summary>
public class LessonQuestionSet
{
    public Guid LessonId { get; set; }
    public Lesson? Lesson { get; set; }

    public Guid LangId { get; set; }
    public Language? Language { get; set; }

    public int Version { get; set; }
}
