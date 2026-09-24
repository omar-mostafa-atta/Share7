using Share7.Application.Assessment.Models;

namespace Share7.Application.Assessment.Interfaces;

/// <summary>
/// Authoring for blueprints and examination specifications. Admin-only, and the only way either
/// gets created — **no examination is seeded by a migration**, because a blueprint is a claim about
/// what a real paper contains and a plausible fabrication wearing the schema's authority is worse
/// than an empty table.
/// </summary>
public interface IBlueprintAuthoringService
{
    Task<IReadOnlyList<BlueprintDto>> ListBlueprintsAsync(
        Guid langId, CancellationToken cancellationToken = default);

    Task<BlueprintDto?> GetBlueprintAsync(
        Guid blueprintId, Guid langId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a blueprint whole — areas and lines together, replacing what was there.
    /// <para>
    /// A published blueprint is **not** edited in place: the write creates the next version
    /// instead, because editing one would silently change the meaning of every coverage figure
    /// ever computed from it.
    /// </para>
    /// </summary>
    Task<AuthoringReport> SaveBlueprintAsync(
        SaveBlueprintRequest request, CancellationToken cancellationToken = default);

    Task<bool> PublishBlueprintAsync(Guid blueprintId, CancellationToken cancellationToken = default);

    Task<AuthoringReport> SaveExamSpecificationAsync(
        SaveExamSpecificationRequest request, CancellationToken cancellationToken = default);

    Task<bool> PublishExamVersionAsync(Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the worked example: a Share7 benchmark over one subject, its chapters as areas and
    /// the targets its lessons teach as lines.
    /// <para>
    /// Exists so the coverage feature has something real to run against in an environment nobody
    /// has authored a syllabus into yet. Its source note says exactly what it is, and its authority
    /// is Share7's own rather than a ministry's — a structural derivation from the platform's own
    /// content is a legitimate benchmark and an illegitimate national paper, and the difference has
    /// to be visible to whoever reads the report.
    /// </para>
    /// </summary>
    Task<AuthoringReport> GenerateBenchmarkAsync(
        GenerateBenchmarkRequest request, Guid langId, CancellationToken cancellationToken = default);
}
