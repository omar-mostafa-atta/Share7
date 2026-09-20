using Share7.Application.Organizations.Models;

namespace Share7.Application.Organizations.Interfaces;

/// <summary>
/// An organization's edits to a curriculum it does not own — authored as a diff, resolved at read
/// time.
///
/// <para><b>Overlay rather than mutation is one of the four independent mechanisms that keep an
/// official curriculum uncorruptible</b> (§17.2), and it is the one that makes the other three
/// tolerable: a school that cannot adapt the national syllabus to how it actually teaches will
/// either not adopt the platform or will work around it, and the workaround is always a copy.</para>
///
/// <para><b>Resolution is a projection, never a write.</b> Applying an overlay produces a view of
/// the official tree for one cohort; it never edits <c>CurriculumNode</c>. That is why evidence
/// collected inside an overlaid cohort names the official version and needs no reconciliation — the
/// overlay changed what the learner was shown and in what order, not what their answer means.</para>
/// </summary>
public interface ICurriculumOverlayService
{
    Task<IReadOnlyList<OverlayDto>> ListAsync(
        Guid orgId, Guid langId, CancellationToken cancellationToken = default);

    Task<OverlayDto?> GetAsync(Guid overlayId, Guid langId, CancellationToken cancellationToken = default);

    Task<OverlayDto> CreateAsync(
        CreateOverlayRequest request, Guid langId, Guid actingUserId,
        CancellationToken cancellationToken = default);

    Task<OverlayDto> AddEditAsync(
        Guid overlayId, AddOverlayEditRequest request, Guid langId,
        CancellationToken cancellationToken = default);

    Task<OverlayDto> RemoveEditAsync(
        Guid overlayId, Guid editId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the overlay so cohorts read through it.
    ///
    /// <para>Refused when an edit names a node outside the overlay's own curriculum version, or
    /// when a substitution points at a node the organization does not own. Both are ways an overlay
    /// could quietly put somebody else's content under an official heading, which is the failure
    /// this whole mechanism exists to prevent.</para>
    /// </summary>
    Task<OverlayDto> PublishAsync(
        Guid overlayId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The children of one node as a cohort sees them: the official order, with the overlay's hides,
    /// reorders, inserts and substitutions applied.
    ///
    /// <para>Returns the official children unchanged when the cohort has no overlay, which is every
    /// cohort until an organization authors one — so a caller has a single code path.</para>
    /// </summary>
    Task<IReadOnlyList<ResolvedNodeDto>> ResolveChildrenAsync(
        Guid cohortId, Guid parentNodeId, Guid langId, CancellationToken cancellationToken = default);
}

/// <summary>
/// One node as a cohort sees it, after its organization's overlay has been applied.
/// </summary>
public sealed record ResolvedNodeDto(
    Guid NodeId,
    string KindKey,
    string Title,
    int Order,
    bool IsPlayable,

    /// <summary>
    /// True when this node is here because the overlay put it here. Surfaced rather than hidden: a
    /// tree that differs from the published curriculum is alarming to a parent unless something
    /// says who changed it.
    /// </summary>
    bool IsOverlaid,

    /// <summary>The official node this stands in for, when it is a substitution.</summary>
    Guid? SubstitutesNodeId,

    DateTime? ScheduledFromUtc,
    DateTime? ScheduledToUtc);
