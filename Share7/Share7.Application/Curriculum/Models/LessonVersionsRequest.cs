namespace Share7.Application.Curriculum.Models;

public class LessonVersionsRequest
{
    /// <summary>Lesson ids the client currently holds in its on-device cache.</summary>
    public List<Guid> LessonIds { get; set; } = [];
}
