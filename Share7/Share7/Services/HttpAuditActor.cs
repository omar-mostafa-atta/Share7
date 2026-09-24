using System.Security.Claims;
using Share7.Application.Audit.Interfaces;

namespace Share7.API.Services;

/// <summary>
/// The audit actor for a request: the signed-in user, the roles in their token, and where the call
/// came from.
/// <para>
/// Roles are read from the token, not looked up, because the token is what authorized the call —
/// the audit row should record the authority the action was actually taken with. A Studio request
/// carries no Identity roles; its Studio role (added by the per-request check, from the database)
/// is recorded as <c>Studio:Author</c> and so on.
/// </para>
/// </summary>
public class HttpAuditActor : IAuditActor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpAuditActor(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    private HttpContext? Context => _httpContextAccessor.HttpContext;

    // Game and admin tokens have "sub" mapped to NameIdentifier; Studio tokens keep it as "sub".
    public Guid? UserId =>
        Guid.TryParse(
            Context?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context?.User.FindFirstValue("sub"),
            out var id)
            ? id
            : null;

    public IReadOnlyList<string> Roles =>
        Context?.User.FindAll(ClaimTypes.Role).Select(c => c.Value)
            .Concat(Context.User.FindAll("studio_role").Select(c => $"Studio:{c.Value}"))
            .Distinct()
            .ToList() ?? [];

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => Context?.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent : null;

    public string? CorrelationId => Context?.TraceIdentifier;
}
