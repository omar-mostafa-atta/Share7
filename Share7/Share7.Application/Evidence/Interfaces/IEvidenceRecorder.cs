using Share7.Application.Evidence.Models;

namespace Share7.Application.Evidence.Interfaces;

/// <summary>
/// Writes educational evidence, and is the only thing that may.
/// <para>
/// <b>Append-only.</b> There is no update and no delete on this interface, deliberately: a response
/// is a historical fact about what a child did, and the correction path for a bad item is to
/// exclude the derived observation, never to edit or remove the response that recorded it.
/// </para>
/// <para>
/// It resolves the evidence contract itself rather than accepting one, so no caller can decide that
/// its own interactions count. A caller with no applicable published contract records nothing and is
/// told so — it is not an error, it is the default state of every interaction in the platform.
/// </para>
/// </summary>
public interface IEvidenceRecorder
{
    /// <summary>
    /// Records one answer per item, deriving attempt ordinal and first-encounter from the log
    /// itself.
    /// <para>
    /// **Does not call SaveChanges.** The rows are added to the caller's change tracker so they
    /// commit inside the caller's transaction — evidence that survived a rolled-back attempt would
    /// describe gameplay that never happened.
    /// </para>
    /// </summary>
    Task<EvidenceRecordingResult> RecordAsync(
        EvidenceRecordingContext context,
        IReadOnlyList<EvidenceAnswer> answers,
        CancellationToken cancellationToken = default);
}
