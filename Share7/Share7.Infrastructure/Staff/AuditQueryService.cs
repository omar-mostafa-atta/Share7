using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Audit;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Staff;

/// <inheritdoc cref="IAuditQueryService"/>
public class AuditQueryService : IAuditQueryService
{
    public const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly PeopleDirectory _people;

    public AuditQueryService(ApplicationDbContext db, PeopleDirectory people)
    {
        _db = db;
        _people = people;
    }

    public async Task<AuditPageDto> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var filtered = Filter(query);

        var total = await filtered.CountAsync(cancellationToken);

        var rows = await filtered
            .OrderByDescending(e => e.Sequence)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new AuditPageDto(await DescribeAsync(rows, cancellationToken), page, size, total);
    }

    public async Task<AuditFacetsDto> GetFacetsAsync(CancellationToken cancellationToken = default)
    {
        var areas = await _db.AuditEvents.AsNoTracking().Select(e => e.Area).Distinct().OrderBy(a => a).ToListAsync(cancellationToken);
        var actions = await _db.AuditEvents.AsNoTracking().Select(e => e.Action).Distinct().OrderBy(a => a).ToListAsync(cancellationToken);
        var actorIds = await _db.AuditEvents.AsNoTracking()
            .Where(e => e.ActorUserId != null)
            .Select(e => e.ActorUserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var names = await _people.NamesAsync(actorIds, cancellationToken);

        return new AuditFacetsDto(
            areas,
            actions,
            names.Select(n => new PersonRefDto(n.Key, n.Value)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    public async Task<string> ExportCsvAsync(AuditQuery query, int maxRows, CancellationToken cancellationToken = default)
    {
        var rows = await Filter(query)
            .OrderByDescending(e => e.Sequence)
            .Take(maxRows)
            .ToListAsync(cancellationToken);

        var described = await DescribeAsync(rows, cancellationToken);
        var csv = new StringBuilder();

        csv.AppendLine("sequence,occurred_at_utc,actor_id,actor_name,actor_roles,area,action,target_type,target_id,summary,data,ip_address,device,correlation_id");

        foreach (var e in described)
        {
            csv.AppendJoin(',',
                    Cell(e.Sequence.ToString(CultureInfo.InvariantCulture)),
                    Cell(e.OccurredAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                    Cell(e.Actor?.UserId.ToString()),
                    Cell(e.Actor?.Name ?? "Platform"),
                    Cell(string.Join(' ', e.ActorRoles)),
                    Cell(e.Area),
                    Cell(e.Action),
                    Cell(e.TargetType),
                    Cell(e.TargetId),
                    Cell(e.Summary),
                    Cell(e.DataJson),
                    Cell(e.IpAddress),
                    Cell(e.Device),
                    Cell(e.CorrelationId))
                .AppendLine();
        }

        return csv.ToString();
    }

    private IQueryable<AuditEvent> Filter(AuditQuery query)
    {
        var events = _db.AuditEvents.AsNoTracking();

        if (query.ActorUserId is { } actor) events = events.Where(e => e.ActorUserId == actor);
        if (!string.IsNullOrWhiteSpace(query.Area)) events = events.Where(e => e.Area == query.Area);
        if (!string.IsNullOrWhiteSpace(query.Action)) events = events.Where(e => e.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.TargetType)) events = events.Where(e => e.TargetType == query.TargetType);
        if (!string.IsNullOrWhiteSpace(query.TargetId)) events = events.Where(e => e.TargetId == query.TargetId);
        if (query.FromUtc is { } from) events = events.Where(e => e.OccurredAtUtc >= from);
        if (query.ToUtc is { } to) events = events.Where(e => e.OccurredAtUtc < to);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            events = events.Where(e => e.Summary.Contains(term) || (e.TargetId != null && e.TargetId.Contains(term)));
        }

        return events;
    }

    private async Task<IReadOnlyList<AuditEventDto>> DescribeAsync(IReadOnlyList<AuditEvent> rows, CancellationToken cancellationToken)
    {
        var names = await _people.NamesAsync(rows.Select(r => r.ActorUserId), cancellationToken);

        return rows.Select(e => new AuditEventDto(
                e.Sequence,
                DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc),
                PeopleDirectory.Ref(names, e.ActorUserId),
                e.ActorRoles.Split(',', StringSplitOptions.RemoveEmptyEntries),
                e.Action,
                e.Area,
                e.TargetType,
                e.TargetId,
                e.Summary,
                e.DataJson,
                e.IpAddress,
                DeviceNames.Describe(e.UserAgent),
                e.CorrelationId))
            .ToList();
    }

    /// <summary>
    /// One CSV cell. Quoted when it must be, and never able to run as a spreadsheet formula: a
    /// leading <c>= + - @</c> (or tab / carriage return) is defused with an apostrophe, because an
    /// audit export is exactly the file somebody opens in Excel without a second thought.
    /// </summary>
    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
