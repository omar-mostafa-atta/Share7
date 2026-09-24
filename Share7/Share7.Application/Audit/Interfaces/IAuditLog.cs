namespace Share7.Application.Audit.Interfaces;

/// <summary>
/// Records what somebody did, in the same unit of work as the doing.
/// <para>
/// <b><see cref="Record"/> does not save.</b> It stages the event on the caller's DbContext, so it
/// commits or rolls back with the change it describes — a refused or failed operation leaves no
/// row claiming it happened, and a committed one cannot be missing its row. Call it immediately
/// before the <c>SaveChangesAsync</c> that commits the change.
/// </para>
/// <para>
/// Who acted, from where, with which roles, comes from <see cref="IAuditActor"/>; callers describe
/// only the action.
/// </para>
/// </summary>
public interface IAuditLog
{
    void Record(AuditEntry entry);
}

/// <summary>
/// One action as a service describes it.
/// </summary>
/// <param name="Action">One of <c>Share7.Domain.Audit.AuditActions</c>.</param>
/// <param name="Area">One of <c>Share7.Domain.Audit.AuditAreas</c>.</param>
/// <param name="Summary">One plain sentence. No names, usernames or emails — ids only.</param>
/// <param name="TargetType">What kind of thing was acted on, e.g. <c>lesson</c>.</param>
/// <param name="TargetId">Its id.</param>
/// <param name="Data">Specifics, serialised to JSON. Ids, counts and flags only.</param>
/// <param name="ActingUserId">
/// Names the actor when the request itself cannot: a member activating their account or signing in
/// is not authenticated yet, but the row must still say it was them. Leave null everywhere else —
/// the request's own actor is the one to trust.
/// </param>
/// <param name="ActingRoles">The roles to record with <paramref name="ActingUserId"/>.</param>
public sealed record AuditEntry(
    string Action,
    string Area,
    string Summary,
    string? TargetType = null,
    string? TargetId = null,
    object? Data = null,
    Guid? ActingUserId = null,
    IReadOnlyList<string>? ActingRoles = null);

/// <summary>
/// Whoever is acting right now. The API reads it from the request; outside a request it is the
/// platform itself.
/// </summary>
public interface IAuditActor
{
    /// <summary>Null when the platform acts on its own behalf.</summary>
    Guid? UserId { get; }

    IReadOnlyList<string> Roles { get; }

    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}
