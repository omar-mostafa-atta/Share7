namespace Share7.Application.Curriculum.Models;

/// <summary>
/// One question as an author works on it: both languages and the pool it belongs to, together.
/// <para>
/// <b>This is the shape a lesson is authored in; it is not the shape it is stored in.</b> Storage
/// keeps the four sets a lesson has — English main, Arabic main, English recovery, Arabic recovery —
/// as independently versioned rows, because a language can be re-uploaded on its own and progress
/// references the rows it graded. Authoring one language at a time is what produced lessons that are
/// playable in English and blank in Arabic, so the sheet pairs them and the pairing is enforced all
/// the way down.
/// </para>
/// <para>
/// <see cref="RowNumber"/> is what makes them one question rather than four. It is the row of the
/// sheet the pair came from, written onto every row of every set the publish produced, so "delete
/// this question" can mean all four of them and not just the one an admin happened to be looking at.
/// </para>
/// </summary>
public class LessonSheetRow
{
    /// <summary>
    /// The sheet row this pair came from, and the key that ties the four stored rows together.
    /// One-based and stable across a republish, because the publish writes the row numbers it is
    /// given rather than re-deriving them from position.
    /// </summary>
    public int RowNumber { get; set; }

    public string QuestionEn { get; set; } = string.Empty;
    public string CorrectEn { get; set; } = string.Empty;
    public string WrongEn1 { get; set; } = string.Empty;
    public string WrongEn2 { get; set; } = string.Empty;

    public string QuestionAr { get; set; } = string.Empty;
    public string CorrectAr { get; set; } = string.Empty;
    public string WrongAr1 { get; set; } = string.Empty;
    public string WrongAr2 { get; set; } = string.Empty;

    /// <summary>
    /// Column 9. True puts the pair in the recovery pool instead of the main one.
    /// <para>
    /// Editable after the fact, and moving a row between pools is a republish of both — which is why
    /// a publish always writes all four sets rather than only the ones it thinks changed.
    /// </para>
    /// </summary>
    public bool IsRecovery { get; set; }
}

/// <summary>A lesson's whole question set, paired, as the console reads it.</summary>
public class LessonSheetDto
{
    public Guid LessonId { get; set; }

    /// <summary>Current published version of each of the four sets. Zero means never published.</summary>
    public int MainVersionEn { get; set; }
    public int MainVersionAr { get; set; }
    public int RecoveryVersionEn { get; set; }
    public int RecoveryVersionAr { get; set; }

    public IReadOnlyList<LessonSheetRow> Rows { get; set; } = [];

    /// <summary>
    /// Rows that exist in one language but not the other, or in one pool's language but not its
    /// pair — the residue of the per-language uploads this replaced.
    /// <para>
    /// Surfaced rather than silently filled with blanks: a half-translated question is a real
    /// authoring problem and the console has to be able to point at it. A paired publish clears
    /// them, because it writes every set.
    /// </para>
    /// </summary>
    public IReadOnlyList<int> UnpairedRowNumbers { get; set; } = [];
}

/// <summary>A full replace of a lesson's question set, both languages and both pools at once.</summary>
public class SaveLessonSheetRequest
{
    public IReadOnlyList<LessonSheetRow> Rows { get; set; } = [];
}

/// <summary>What a paired publish did, per set.</summary>
public class LessonSheetResult
{
    public bool Succeeded { get; set; }
    public Guid LessonId { get; set; }

    public int MainCount { get; set; }
    public int RecoveryCount { get; set; }

    public int MainVersion { get; set; }
    public int RecoveryVersion { get; set; }

    /// <summary>Rows retired across all four sets by this publish.</summary>
    public int ReplacedCount { get; set; }

    public IReadOnlyList<QuestionImportError> Errors { get; set; } = [];

    public static LessonSheetResult Failed(Guid lessonId, params string[] messages) => new()
    {
        Succeeded = false,
        LessonId = lessonId,
        Errors = [.. messages.Select(m => new QuestionImportError { Message = m })]
    };
}
