namespace Share7.Application.Structure.Interfaces;

/// <summary>
/// Keeps <c>CurriculumNodes</c> in step with the legacy typed tree.
/// <para>
/// **The projection is derived, and this is the only thing that writes it.** Grade / Term /
/// Subject / Chapter / Lesson remain the source of truth while shipped clients still read them;
/// the node table is the generic shape everything new reads, built from the same rows and keeping
/// their exact ids. Nothing can disagree, because nothing is duplicated except the shape.
/// </para>
/// <para>
/// A full rebuild rather than an incremental patch: the tree is a few thousand rows, a rebuild is
/// idempotent, and "run it again if you are unsure" is worth more than the milliseconds a diff
/// would save. Deleted nodes are **retired, never removed** — evidence and mappings name them.
/// </para>
/// </summary>
public interface ICurriculumProjector
{
    /// <summary>Brings the node table into agreement with the typed tables. Idempotent.</summary>
    Task<CurriculumProjectionReport> SyncAsync(CancellationToken cancellationToken = default);
}

/// <summary>What one sync changed, so an admin surface can say something specific.</summary>
public sealed record CurriculumProjectionReport
{
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Retired { get; init; }
    public int Restored { get; init; }
    public int TranslationsWritten { get; init; }
    public int Total { get; init; }

    public bool ChangedAnything => Added + Updated + Retired + Restored + TranslationsWritten > 0;
}
