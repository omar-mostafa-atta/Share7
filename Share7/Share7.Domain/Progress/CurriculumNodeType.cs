namespace Share7.Domain.Progress;

/// <summary>
/// Which level of the tree a <see cref="UserNodeUnlock"/> row refers to.
/// <para>
/// Grades are absent on purpose: a student is pinned to <c>StudentProfile.GradeId</c> and only
/// ever sees their own grade, so a grade-level lock would never do anything. The ladder tops
/// out at Term.
/// </para>
/// </summary>
public enum CurriculumNodeType
{
    Term = 1,
    Subject = 2,
    Chapter = 3,
    Lesson = 4
}
