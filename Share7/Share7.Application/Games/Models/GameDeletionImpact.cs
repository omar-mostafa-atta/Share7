namespace Share7.Application.Games.Models;

/// <summary>
/// What deleting a game would destroy. Returned with the 409 so an admin sees "this wipes 312
/// students' progress" before committing.
/// </summary>
public class GameDeletionImpact
{
    public int Students { get; set; }
    public int LessonProgressRows { get; set; }
    public int QuestionProgressRows { get; set; }
    public int Unlocks { get; set; }

    public bool HasProgress => LessonProgressRows > 0 || QuestionProgressRows > 0 || Unlocks > 0;

    public string Describe()
    {
        if (!HasProgress)
            return "no recorded progress";

        return $"{LessonProgressRows} lesson progress row(s), {QuestionProgressRows} question " +
               $"progress row(s) and {Unlocks} unlock(s) across {Students} student(s)";
    }
}
