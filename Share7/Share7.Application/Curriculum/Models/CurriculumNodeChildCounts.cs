namespace Share7.Application.Curriculum.Models;

/// <summary>
/// What a delete would destroy. Returned with the 409 when a node still has descendants, so
/// the admin UI can show "this removes 3 chapters, 12 lessons and 240 questions" before the
/// operator commits to it.
/// </summary>
public class CurriculumNodeChildCounts
{
    public int Subjects { get; set; }
    public int Chapters { get; set; }
    public int Lessons { get; set; }
    public int Questions { get; set; }

    public bool HasChildren => Subjects > 0 || Chapters > 0 || Lessons > 0 || Questions > 0;

    /// <summary>Human-readable summary, e.g. "2 subjects, 5 chapters, 11 lessons, 300 questions".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Subjects > 0) parts.Add($"{Subjects} subject(s)");
        if (Chapters > 0) parts.Add($"{Chapters} chapter(s)");
        if (Lessons > 0) parts.Add($"{Lessons} lesson(s)");
        if (Questions > 0) parts.Add($"{Questions} question(s)");
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }
}
