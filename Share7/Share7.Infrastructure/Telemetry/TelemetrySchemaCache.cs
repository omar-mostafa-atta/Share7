using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Telemetry;

/// <summary>
/// The event registry, held for the process rather than read per request.
/// <para>
/// **Ingest reads the whole registry on every batch.** At a million DAU that is thousands of reads
/// a second against a table of a few dozen authored rows that change roughly never — the same
/// argument <c>LevelCurveCache</c> makes about the level curve, only louder, because this is on
/// the hottest path in the platform rather than on an attempt.
/// </para>
/// <para>
/// **Refreshed on a TTL rather than invalidated on write.** An operator disabling an event or
/// dropping its sampling rate expects it to take effect promptly, not instantly, and a cache that
/// several app instances have to be told about is a distributed invalidation problem this does not
/// need. A minute of staleness costs a minute of events at the old rate.
/// </para>
/// </summary>
public interface ITelemetrySchemaCache
{
    Task<IReadOnlyDictionary<string, TelemetryEventSchema>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Drops the cached copy so the next read reloads. Called after an operator edits the registry.</summary>
    void Invalidate();
}

public class TelemetrySchemaCache : ITelemetrySchemaCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// One in-flight load shared by every caller.
    /// <para>
    /// Without it, a cold start under load has every concurrent batch issue its own query — a
    /// thundering herd against the database at exactly the moment the app is least able to absorb
    /// one. The same reason the admin console's API client shares a single refresh promise.
    /// </para>
    /// </summary>
    private Task<IReadOnlyDictionary<string, TelemetryEventSchema>>? _inFlight;

    private IReadOnlyDictionary<string, TelemetryEventSchema>? _cached;
    private DateTime _loadedAtUtc;
    private readonly object _gate = new();

    public TelemetrySchemaCache(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public Task<IReadOnlyDictionary<string, TelemetryEventSchema>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_cached is not null && DateTime.UtcNow - _loadedAtUtc < Ttl)
                return Task.FromResult(_cached);

            return _inFlight ??= LoadAsync();
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
            _loadedAtUtc = default;
        }
    }

    private async Task<IReadOnlyDictionary<string, TelemetryEventSchema>> LoadAsync()
    {
        try
        {
            // Its own scope: this is a singleton, and taking a scoped DbContext on one would keep
            // the first request's context alive for the life of the process. The hazard
            // MultiplayerCompositionTests exists to catch.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var rows = await dbContext.TelemetryEventSchemas
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Name, StringComparer.Ordinal);

            lock (_gate)
            {
                _cached = rows;
                _loadedAtUtc = DateTime.UtcNow;
                _inFlight = null;
            }

            return rows;
        }
        catch
        {
            lock (_gate)
            {
                _inFlight = null;

                // Serve the stale copy rather than failing ingest. A registry that cannot be read
                // is a database problem; refusing a child's events over it would lose real data for
                // the duration of an outage that has nothing to do with them.
                if (_cached is not null) return _cached;
            }

            throw;
        }
    }
}
