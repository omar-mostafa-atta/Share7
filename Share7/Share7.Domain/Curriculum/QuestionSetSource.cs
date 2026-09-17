namespace Share7.Domain.Curriculum;

/// <summary>
/// How one published question version was authored.
/// <para>
/// The upload tables exist to answer "why did this question set change", and "an admin typed it"
/// is a different answer from "an admin uploaded a sheet". Without this column the two are
/// indistinguishable: a manual publish has no file name to record, and a blank <c>FileName</c>
/// reads as missing data rather than as a deliberate absence.
/// </para>
/// <para>
/// Shared by both pools — the main question set and the recovery set are authored the same two
/// ways, so one enum rather than two that would have to be kept in step.
/// </para>
/// </summary>
public enum QuestionSetSource
{
    /// <summary>
    /// Published from an .xlsx sheet. <c>FileName</c> carries the uploaded file's name.
    /// <para>
    /// Deliberately the zero value: every row written before this column existed came from an
    /// upload, so the default backfills history correctly rather than marking it unknown.
    /// </para>
    /// </summary>
    ExcelUpload = 0,

    /// <summary>Typed into the admin console by hand. <c>FileName</c> is empty.</summary>
    ManualEntry
}
