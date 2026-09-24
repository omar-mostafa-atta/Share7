using Share7.Application.Measurement.Models;

namespace Share7.Application.Measurement.Interfaces;

/// <summary>
/// Reads what children's answers say about the content itself.
/// <para>
/// **This is the first surface where the evidence layer pays for itself**, and it pays an admin
/// rather than a learner. Long before anybody can be told how good they are at mathematics, the
/// platform can say which questions are mis-keyed, which distractors nobody ever picks, which
/// items are being answered too fast to have been read, and which content is mapped to no learning
/// target and therefore cannot be measured at all. Every one of those is actionable today and none
/// of them requires a psychometric model — <c>Docs/EducationalArchitecture.md</c> §16.4.
/// </para>
/// </summary>
public interface IContentQualityService
{
    Task<ContentQualitySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Items ordered worst-first by default, so the surface opens on the thing worth fixing rather
    /// than on the alphabet.
    /// </summary>
    Task<IReadOnlyList<ItemQualityDto>> GetItemsAsync(
        Guid langId,
        string? flag = null,
        Guid? nodeId = null,
        int take = 50,
        int skip = 0,
        CancellationToken cancellationToken = default);

    Task<ItemQualityDto?> GetItemAsync(
        Guid itemId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks or unmarks an item as an anchor.
    /// <para>
    /// **Cheap now, impossible retroactively.** An item only works as an anchor if it was already
    /// being administered across cohorts and forms while the data accumulated; deciding in two
    /// years that a question should have been one does not produce the responses needed to link
    /// the scales (§4.5).
    /// </para>
    /// </summary>
    Task<bool> SetAnchorAsync(Guid itemId, bool isAnchor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Excludes every observation of one item version, stamped with a reason and a reviewer.
    /// <para>
    /// **The responses are not touched.** The child did answer; that is a fact and it stays. What
    /// changes is whether the answer is allowed to inform a measurement — and the row explains,
    /// permanently, why the numbers moved. §12.3.
    /// </para>
    /// </summary>
    Task<int> ExcludeItemObservationsAsync(
        Guid itemVersionId,
        Domain.Measurement.ObservationExclusionReason reason,
        Guid? excludedByUserId,
        string? note,
        CancellationToken cancellationToken = default);
}
