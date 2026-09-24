using Share7.Application.Assessment.Models;
using Share7.Application.Common.Models;
using Share7.Application.Workspace.Models;

namespace Share7.Application.Studio.Interfaces;

/// <summary>
/// Blueprints, as the content team reads them: what a paper is supposed to cover, and whether the
/// question bank could actually serve it.
/// <para>
/// **A blueprint is a claim about what a real examination contains**, so nothing here invents one.
/// The team reads what exists, sees the two things that would make a blueprint unusable — lines
/// naming a stand-in rather than a real skill, and lines no question in the bank can satisfy — and
/// either fixes the content or, where nobody has authored a syllabus at all, generates Share7's own
/// benchmark from one subject and is told plainly that that is what it is.
/// </para>
/// <para>
/// **Publishing freezes it.** A published blueprint is never edited: every coverage figure ever
/// computed from it would quietly change meaning. A change makes the next version instead.
/// </para>
/// </summary>
public interface IStudioExamsService
{
    Task<IReadOnlyList<BlueprintDto>> BlueprintsAsync(Guid langId, CancellationToken cancellationToken = default);

    Task<ServiceResult<BlueprintDto>> BlueprintAsync(
        Guid blueprintId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds Share7's own benchmark over one subject: its chapters as the paper's areas, the
    /// skills its lessons teach as the lines.
    /// <para>
    /// Its source note says exactly what it is. A structural derivation from the platform's own
    /// content is a legitimate benchmark and an illegitimate national paper, and which one a reader
    /// is looking at has to be visible on the page rather than inferred.
    /// </para>
    /// </summary>
    Task<ServiceResult<BenchmarkReportDto>> GenerateBenchmarkAsync(
        StudioMember member, Guid subjectNodeId, string? versionLabel, Guid langId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Freezes a blueprint. Refused while any line still names a stand-in: a coverage figure
    /// computed against a lesson wearing a skill's clothes is a completion percentage dressed as a
    /// proficiency claim, which is the one thing this architecture exists to prevent.
    /// </summary>
    Task<ServiceResult<BlueprintDto>> PublishAsync(
        StudioMember member, Guid blueprintId, Guid langId, CancellationToken cancellationToken = default);
}

public sealed record BenchmarkReportDto
{
    public required Guid BlueprintId { get; init; }
    public required string BlueprintKey { get; init; }
    public required int Areas { get; init; }
    public required int Lines { get; init; }

    /// <summary>Lines naming a stand-in. While this is non-zero the blueprint proves nothing.</summary>
    public required int LinesOnStandIns { get; init; }

    public string? Warning { get; init; }
}
