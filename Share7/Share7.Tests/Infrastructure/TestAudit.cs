using Share7.Application.Audit.Interfaces;
using Share7.Infrastructure.Audit;
using Share7.Infrastructure.Persistence;

namespace Share7.Tests.Infrastructure;

/// <summary>
/// The real audit log over the test database, acting as the platform — what a service gets outside
/// an HTTP request. Tests that care who acted pass their own <see cref="IAuditActor"/>.
/// </summary>
public static class TestAudit
{
    public static IAuditLog For(ApplicationDbContext context, IAuditActor? actor = null) =>
        new AuditLog(context, actor ?? SystemAuditActor.Instance);
}

/// <summary>A signed-in actor with fixed details, for asserting what an audit row records.</summary>
public sealed record TestAuditActor(
    Guid? UserId,
    IReadOnlyList<string> Roles,
    string? IpAddress = "203.0.113.7",
    string? UserAgent = "Share7Tests/1.0",
    string? CorrelationId = "test-correlation") : IAuditActor;
