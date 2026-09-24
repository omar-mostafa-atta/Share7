using System.Text.Json;
using Share7.Application.Audit.Interfaces;
using Share7.Domain.Audit;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Audit;

/// <inheritdoc cref="IAuditLog"/>
public class AuditLog : IAuditLog
{
    private static readonly JsonSerializerOptions DataJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditActor _actor;

    public AuditLog(ApplicationDbContext dbContext, IAuditActor actor)
    {
        _dbContext = dbContext;
        _actor = actor;
    }

    public void Record(AuditEntry entry)
    {
        _dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow,
            ActorUserId = entry.ActingUserId ?? _actor.UserId,
            ActorRoles = Clip(string.Join(",", (entry.ActingUserId is null ? _actor.Roles : entry.ActingRoles ?? []).Order(StringComparer.Ordinal)), 200),
            Action = entry.Action,
            Area = entry.Area,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            Summary = Clip(entry.Summary, 500),
            DataJson = entry.Data is null ? null : JsonSerializer.Serialize(entry.Data, DataJson),
            IpAddress = Clip(_actor.IpAddress, 45),
            UserAgent = Clip(_actor.UserAgent, 256),
            CorrelationId = Clip(_actor.CorrelationId, 64)
        });
    }

    /// <summary>
    /// Trims to the column width rather than failing. An over-long user agent must never be the
    /// reason a curriculum change is refused — the change is the point, the row describes it.
    /// </summary>
    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
    private static string? Clip(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>
/// The platform acting on its own behalf: startup seeding, background jobs, and any caller outside
/// an HTTP request. The API host replaces it with the request's actor.
/// </summary>
public sealed class SystemAuditActor : IAuditActor
{
    public static readonly SystemAuditActor Instance = new();

    public Guid? UserId => null;
    public IReadOnlyList<string> Roles => [];
    public string? IpAddress => null;
    public string? UserAgent => null;
    public string? CorrelationId => null;
}
