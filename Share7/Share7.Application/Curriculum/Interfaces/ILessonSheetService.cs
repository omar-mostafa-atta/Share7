using Share7.Application.Curriculum.Models;

namespace Share7.Application.Curriculum.Interfaces;

/// <summary>
/// A lesson's questions as one authored object: both languages, both pools, one upload, one save.
/// <para>
/// <b>Why this exists next to the per-language importers rather than instead of them.</b> The old
/// path publishes one language of one pool at a time, which is the correct primitive — a translator
/// finishing Arabic must not have to re-send English — and it is what <c>LessonQuestionSet</c> is
/// versioned for. What it is not is a usable authoring surface: a lesson needs four uploads to be
/// complete, nothing checks that they line up, and the failure mode is a lesson that plays in one
/// language and is blank in the other. This wraps the same storage in the unit an author actually
/// works in.
/// </para>
/// <para>
/// <b>Every write here is a full replace of all four sets.</b> Partial publishes are what let the
/// sets drift apart, and a paired model that can be half-written is not paired.
/// </para>
/// </summary>
public interface ILessonSheetService
{
    /// <summary>
    /// A lesson's active questions, paired by row number, with the version of each of the four sets.
    /// </summary>
    /// <returns>Null when no lesson has that id.</returns>
    Task<LessonSheetDto?> GetAsync(Guid lessonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a 9-column workbook: English question and three answers, the same in Arabic, then a
    /// recovery flag.
    /// <para>
    /// Refused whole if any row fails validation, and refused whole if no row is flagged as
    /// recovery — a lesson with no recovery pool has nothing to offer a child who got it wrong, and
    /// accepting one is how the gap gets discovered in production instead of at upload.
    /// </para>
    /// </summary>
    Task<LessonSheetResult> ImportAsync(
        Guid lessonId,
        Stream excelStream,
        string fileName,
        bool hasHeaderRow = true,
        Guid? uploadedByUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a lesson's whole set from rows typed in the console. Same validation as the upload,
    /// including the recovery requirement.
    /// </summary>
    Task<LessonSheetResult> SaveAsync(
        Guid lessonId,
        SaveLessonSheetRequest request,
        Guid? savedByUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An empty nine-column workbook with the headers filled in.
    /// <para>
    /// Served rather than documented because the columns are positional and unlabelled in the
    /// parser — describing them in a tooltip and hoping is how a sheet arrives with Arabic in the
    /// English columns.
    /// </para>
    /// </summary>
    byte[] BuildTemplate();

    /// <summary>
    /// Removes one question in every language and both pools — every instance of it — and republishes
    /// what is left.
    /// <para>
    /// Refused when it would empty the recovery pool, for the same reason an upload with no recovery
    /// row is refused. Deleting the last main question is allowed: a lesson with no questions is
    /// simply unpublished, which is a state the client already understands.
    /// </para>
    /// </summary>
    Task<LessonSheetResult> DeleteRowAsync(
        Guid lessonId,
        int rowNumber,
        Guid? deletedByUserId = null,
        CancellationToken cancellationToken = default);
}
