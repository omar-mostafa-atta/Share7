using System.Text.Json;

namespace Share7.Infrastructure.Runs;

/// <summary>
/// One serializer configuration for everything stored on a run, shared by the writer and every
/// reader.
/// <para>
/// **Not a tidiness measure — the default options are actively wrong here.** <c>PickupsJson</c> is
/// written from anonymous objects, which System.Text.Json emits as <c>{"kind":…,"count":…}</c>, while
/// reads are case-*sensitive* by default and bind <c>Kind</c>/<c>Count</c> to nothing. The result is
/// not an exception but a silent one: a replayed settlement comes back reporting
/// <c>{ kind: null, count: 0 }</c> for pickups the child really did collect, and the only symptom is
/// a results screen showing zero after a reconnect.
/// </para>
/// <para>
/// <c>JsonSerializerDefaults.Web</c> makes the write camelCase and the read case-insensitive, so the
/// two halves cannot disagree by convention. Every read and write of run JSON goes through here.
/// </para>
/// </summary>
internal static class RunJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
