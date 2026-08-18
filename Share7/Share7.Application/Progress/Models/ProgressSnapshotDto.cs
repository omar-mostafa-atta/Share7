namespace Share7.Application.Progress.Models;

/// <summary>
/// The whole tree for one grade with availability and completion on every node — the shape
/// Unity's <c>CurriculumSnapshot</c> wants on game open. One call instead of a request per
/// lesson.
/// </summary>
public class ProgressSnapshotDto
{
    public Guid GameId { get; set; }
    public Guid LangId { get; set; }
    public Guid GradeId { get; set; }
    public string GradeName { get; set; } = string.Empty;

    /// <summary>Overall percent across every playable lesson in the grade.</summary>
    public int Percent { get; set; }

    public List<SnapshotTermDto> Terms { get; set; } = [];
}

public class SnapshotTermDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsUnlocked { get; set; }
    public int Percent { get; set; }
    public List<SnapshotSubjectDto> Subjects { get; set; } = [];
}

public class SnapshotSubjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsUnlocked { get; set; }
    public int Percent { get; set; }
    public List<SnapshotChapterDto> Chapters { get; set; } = [];
}

public class SnapshotChapterDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsUnlocked { get; set; }
    public int Percent { get; set; }
    public List<SnapshotLessonDto> Lessons { get; set; } = [];
}

public class SnapshotLessonDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }

    public bool IsUnlocked { get; set; }

    /// <summary>
    /// False when no sheet has been uploaded for this lesson in the student's language. It is
    /// named but unplayable, and it does not block the unlock chain.
    /// </summary>
    public bool HasQuestions { get; set; }

    public string CompletionState { get; set; } = nameof(Domain.Progress.CompletionState.Uncompleted);
    public int Percent { get; set; }
    public int Attempts { get; set; }
    public bool ContentUpdated { get; set; }
}
