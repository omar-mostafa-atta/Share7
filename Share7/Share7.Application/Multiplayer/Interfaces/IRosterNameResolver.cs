namespace Share7.Application.Multiplayer.Interfaces;

/// <summary>
/// The name each seat in a roster is shown under.
/// <para>
/// **A seam, because this is a child-safety decision rather than a formatting one.** A public match
/// seats strangers side by side, so what goes on the seat is governed by the same rule the
/// leaderboards already follow: a generated handle that is derived from nothing personal. When a
/// friends system and guardian consent exist, "friends see my real first name" is a new
/// implementation of this interface — not a change to every session endpoint.
/// </para>
/// </summary>
public interface IRosterNameResolver
{
    /// <summary>
    /// A name for each of <paramref name="userIds"/>. An account with no name to show is absent, and
    /// the roster renders it as null — never as an empty string the client would have to special-case.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}
