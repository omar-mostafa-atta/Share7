using Share7.Application.Assessment.Models;

namespace Share7.Application.Assessment.Interfaces;

/// <summary>
/// Runs sittings: opens an administration, serves its positions, grades each answer server-side
/// and closes it.
/// <para>
/// Every answer taken here goes through the same evidence path as a gameplay answer — one
/// <c>LearnerResponse</c> naming a published evidence contract — and so lands in the same log and
/// the same measurements. **There is no second evidence pipeline for assessments**, which is what
/// makes an exam sitting and a runner level comparable evidence about the same child rather than
/// two incompatible histories (§3.1).
/// </para>
/// </summary>
public interface IAssessmentService
{
    /// <summary>
    /// Opens a sitting, or returns the one this idempotency key already opened. The conditions are
    /// fixed here and cannot be changed afterwards — they are the basis of every claim the
    /// resulting evidence will support.
    /// </summary>
    Task<AdministrationDto> StartAsync(
        Guid learnerId, StartAdministrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>The sitting as it stands, for resumption or for a result screen.</summary>
    Task<AdministrationDto?> GetAsync(
        Guid administrationId, Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The paper, in order, in the sitting's language. Correctness is never included — the client
    /// has no field in which to learn the answer before the learner does.
    /// </summary>
    Task<IReadOnlyList<AdministrationItemDto>> GetItemsAsync(
        Guid administrationId, Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grades one answer, writes the evidence, and says what happened. Rejects rather than throws
    /// when the sitting has expired or the position is already answered — both are ordinary
    /// outcomes of a real client on a real network.
    /// </summary>
    Task<AdministrationAnswerResult> AnswerAsync(
        Guid administrationId, Guid learnerId, AdministrationAnswerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the sitting and computes its score. Unreached positions are recorded as
    /// <c>NoResponse</c>, **not as wrong** — a learner who ran out of time did not answer those
    /// questions incorrectly, and the distinction is the difference between measuring knowledge
    /// and measuring speed.
    /// </summary>
    Task<AdministrationDto?> CompleteAsync(
        Guid administrationId, Guid learnerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a form from a blueprint by drawing items mapped to each line's target, inside its
    /// difficulty window. Reports honestly when a line cannot be filled rather than quietly
    /// producing a shorter paper that no longer matches its own specification.
    /// </summary>
    Task<FormGenerationReport> GenerateFormAsync(
        Guid assessmentId, Guid blueprintId, CancellationToken cancellationToken = default);
}

/// <summary>What form generation managed, and what it could not.</summary>
public sealed record FormGenerationReport
{
    public required Guid FormId { get; init; }
    public required int FormNumber { get; init; }
    public required int ItemsPlaced { get; init; }

    /// <summary>
    /// Lines the bank could not satisfy, with what was asked and what was available. **Reported
    /// rather than silently skipped** — a paper that is short on geometry because no geometry items
    /// exist is not the paper the blueprint describes, and shipping it as though it were is how an
    /// assessment quietly stops measuring what it claims.
    /// </summary>
    public required IReadOnlyList<UnfilledLine> Unfilled { get; init; }
}

public sealed record UnfilledLine
{
    public required Guid LineId { get; init; }
    public required Guid TargetId { get; init; }
    public required string Statement { get; init; }
    public required int Requested { get; init; }
    public required int Available { get; init; }
}
