using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Telemetry;
using Share7.Application.Telemetry.Interfaces;
using Share7.Application.Telemetry.Models;
using Share7.Domain.Telemetry;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Telemetry;

/// <summary>
/// Turns one client batch into rows.
/// <para>
/// **Everything expensive is somebody else's job.** This validates, scrubs, clamps, de-duplicates
/// and does one <c>AddRange</c> plus one <c>SaveChanges</c>. It updates no counter, touches no
/// rollup and computes no derived number — a contended row in front of a million concurrent
/// writers is the failure mode the entire rollup design exists to avoid.
/// </para>
/// <para>
/// **A batch is never all-or-nothing.** Valid events are stored even when others alongside them are
/// refused, because failing the batch would let one malformed event on a shipped build cost every
/// other event that device queues behind it — permanently, since the client would retry the same
/// batch forever.
/// </para>
/// </summary>
public class TelemetryIngestService : ITelemetryIngestService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITelemetrySchemaCache _schemas;
    private readonly TelemetryOptions _options;
    private readonly ILogger<TelemetryIngestService> _logger;

    public TelemetryIngestService(
        ApplicationDbContext dbContext,
        ITelemetrySchemaCache schemas,
        IOptions<TelemetryOptions> options,
        ILogger<TelemetryIngestService> logger)
    {
        _dbContext = dbContext;
        _schemas = schemas;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<TelemetryBatchResponse>> IngestAsync(
        Guid userId,
        TelemetryBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            // Shed rather than refuse. The client is told to come back later and keeps its queue;
            // a 4xx here would have it drop events the pipeline was only temporarily not taking.
            return ServiceResult<TelemetryBatchResponse>.Success(new TelemetryBatchResponse
            {
                MaxBatchSize = _options.MaxBatchSize,
                RetryAfterSeconds = 300
            });
        }

        if (request.Events.Count == 0)
            return ServiceResult<TelemetryBatchResponse>.Invalid("A batch must carry at least one event.");

        if (request.SessionId == Guid.Empty)
            return ServiceResult<TelemetryBatchResponse>.Invalid("A batch must name the client session it came from.");

        if (request.Events.Count > _options.MaxBatchSize)
        {
            // Refused rather than truncated: silently dropping the tail would make the client
            // believe events were stored that were not, and it would never retry them.
            return ServiceResult<TelemetryBatchResponse>.Invalid(
                $"A batch may carry at most {_options.MaxBatchSize} events.");
        }

        var receivedAt = DateTime.UtcNow;
        var earliestAllowed = receivedAt.AddDays(-_options.MaxBacklogDays);

        var context = request.Context;
        var appVersion = Trim(context.AppVersion, 32);
        var platform = Trim(context.Platform, TelemetryPlatforms.MaxLength).ToLowerInvariant();
        var deviceModel = string.IsNullOrWhiteSpace(context.DeviceModel) ? null : Trim(context.DeviceModel, 64);
        var locale = string.IsNullOrWhiteSpace(context.Locale) ? null : Trim(context.Locale, 16);

        var schemas = await _schemas.GetAllAsync(cancellationToken);

        var rejected = new List<TelemetryRejectionDto>();
        var candidates = new List<TelemetryEvent>(request.Events.Count);
        var seenInBatch = new HashSet<Guid>();
        var newUnregisteredNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dto in request.Events)
        {
            if (dto.Id == Guid.Empty)
            {
                rejected.Add(Reject(dto, TelemetryRejectReasons.Malformed, "blank event id"));
                continue;
            }

            if (!TelemetryPrivacy.IsValidEventName(dto.Name))
            {
                rejected.Add(Reject(dto, TelemetryRejectReasons.InvalidName, "names must be snake_case"));
                continue;
            }

            schemas.TryGetValue(dto.Name, out var schema);

            if (schema is { Enabled: false })
            {
                // An authored refusal, and the client is told so it stops sending. Accepting and
                // discarding would leave a shipped build spending a child's battery on rows nobody
                // keeps — which is the thing turning an event off is meant to prevent.
                rejected.Add(Reject(dto, TelemetryRejectReasons.SchemaDisabled, "disabled in the registry"));
                continue;
            }

            var unregistered = schema is null;

            if (unregistered)
            {
                // Cap distinct *new* names, not events. A release adds a handful of names at once;
                // a hundred in one batch is a broken build, and letting it through is how a registry
                // fills with garbage nobody can tell from real vocabulary six years later.
                if (!newUnregisteredNames.Contains(dto.Name) &&
                    newUnregisteredNames.Count >= _options.MaxUnregisteredNamesPerBatch)
                {
                    rejected.Add(Reject(dto, TelemetryRejectReasons.UnregisteredFlood,
                        "too many unregistered names in one batch"));
                    continue;
                }

                newUnregisteredNames.Add(dto.Name);
            }

            if (!TryBuildParams(dto, out var paramsJson, out var paramFailure))
            {
                rejected.Add(Reject(dto, paramFailure!.Value.Reason, paramFailure.Value.Detail));
                continue;
            }

            // Two events with the same id inside one batch: keep the first. The unique index would
            // otherwise fail the whole SaveChanges over a client-side duplicate.
            if (!seenInBatch.Add(dto.Id)) continue;

            // The clamp. A child's tablet clock can be years out — the same problem Run.DurationMs
            // already clamps for — and an unclamped value silently drops events into a cohort they
            // do not belong to. Clamped, not refused: the events are real, only the clock is wrong.
            var occurredAt = dto.OccurredAtUtc;
            if (occurredAt > receivedAt) occurredAt = receivedAt;
            if (occurredAt < earliestAllowed) occurredAt = earliestAllowed;

            candidates.Add(new TelemetryEvent
            {
                Id = dto.Id,
                UserId = userId,
                SessionId = request.SessionId,
                Name = dto.Name,

                // Copied from the registry at ingest rather than joined later, so a category changed
                // next year cannot retroactively re-classify events collected under the old basis.
                // An unregistered event is Behavioural — the more restrictive of the two.
                Category = schema?.Category ?? TelemetryCategory.Behavioural,

                OccurredAtUtc = occurredAt,
                ReceivedAtUtc = receivedAt,
                DayUtc = receivedAt.Date,
                ClientSeq = dto.ClientSeq,
                AppVersion = appVersion,
                Platform = platform,
                DeviceModel = deviceModel,
                Locale = locale,
                GameId = dto.GameId,
                RunId = dto.RunId,
                ParamsJson = paramsJson,
                SampleRate = dto.SampleRate is > 0 and <= 1 ? dto.SampleRate : 1.0,
                IsUnregistered = unregistered
            });
        }

        var duplicates = 0;

        if (candidates.Count > 0)
        {
            // One round trip to find what is already stored, rather than letting the unique index
            // throw. A replay is the *ordinary* path here — the offline queue retries on reconnect
            // by design — so treating it as an exception would make the common case the expensive one.
            var ids = candidates.Select(c => c.Id).ToList();

            var existing = await _dbContext.TelemetryEvents
                .Where(e => e.UserId == userId && ids.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            if (existing.Count > 0)
            {
                var existingSet = existing.ToHashSet();
                duplicates = candidates.RemoveAll(c => existingSet.Contains(c.Id));
            }
        }

        if (candidates.Count > 0)
        {
            _dbContext.TelemetryEvents.AddRange(candidates);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Two devices flushing the same queued event at once loses the race here rather than
                // in the read above. Counted as duplicates, because that is what it is — and never
                // surfaced as a failure, or the client would retry a batch that is already stored.
                _logger.LogDebug(ex, "Telemetry batch collided on the idempotency index; treating as replay.");

                _dbContext.ChangeTracker.Clear();
                duplicates += candidates.Count;
                candidates.Clear();
            }
        }

        if (newUnregisteredNames.Count > 0)
            await RecordUnregisteredAsync(newUnregisteredNames, receivedAt, cancellationToken);

        return ServiceResult<TelemetryBatchResponse>.Success(new TelemetryBatchResponse
        {
            Accepted = candidates.Count,
            Duplicates = duplicates,
            Rejected = rejected,
            MaxBatchSize = _options.MaxBatchSize,
            Sampling = BuildSampling(request.Events, schemas)
        });
    }

    /// <summary>
    /// Notes a name the registry has never seen, so the console can offer it for registration.
    /// <para>
    /// The row is created **disabled for rollups but enabled for ingest**: the events keep landing
    /// (losing real data because a client shipped ahead of a registry row is the worse failure) and
    /// nothing folds them into a metric until a human says what they are. See Rule 6.
    /// </para>
    /// </summary>
    private async Task RecordUnregisteredAsync(
        IReadOnlyCollection<string> names, DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            var known = await _dbContext.TelemetryEventSchemas
                .Where(s => names.Contains(s.Name))
                .Select(s => s.Name)
                .ToListAsync(cancellationToken);

            var knownSet = known.ToHashSet(StringComparer.Ordinal);
            var added = false;

            foreach (var name in names)
            {
                if (knownSet.Contains(name)) continue;

                _dbContext.TelemetryEventSchemas.Add(new TelemetryEventSchema
                {
                    Name = name,
                    Group = "unregistered",
                    Description = "Seen in the wild before it was registered.",
                    Category = TelemetryCategory.Behavioural,
                    SampleRate = 1.0,
                    Enabled = true,
                    RollUpDaily = false,
                    Dimensions = string.Empty,
                    FirstSeenAtUtc = now,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });

                added = true;
            }

            if (added) await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two ingests racing to register the same new name. Harmless — one of them won, the row
            // exists, and the events are already stored. Never worth failing an accepted batch over.
            _dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Validates and re-serialises the parameter bag.
    /// <para>
    /// Re-serialised rather than passed through, so what is stored is exactly what was checked —
    /// storing the client's raw string would leave the door open to a payload whose checked form
    /// and stored form differ.
    /// </para>
    /// </summary>
    private bool TryBuildParams(
        TelemetryEventDto dto, out string json, out (string Reason, string Detail)? failure)
    {
        json = "{}";
        failure = null;

        if (dto.Params is not { Count: > 0 }) return true;

        if (dto.Params.Count > _options.MaxParamsPerEvent)
        {
            failure = (TelemetryRejectReasons.PayloadTooLarge,
                $"at most {_options.MaxParamsPerEvent} parameters per event");
            return false;
        }

        var scrubbed = new Dictionary<string, object?>(dto.Params.Count, StringComparer.Ordinal);

        foreach (var (key, value) in dto.Params)
        {
            if (!TelemetryPrivacy.IsAllowedParameter(key, out var reason))
            {
                // Refused, not stripped. A silently dropped field is a gap discovered months later
                // by whoever tries to answer a question with it — by which time the build that
                // produced it is on a million devices. See TelemetryPrivacy.
                failure = (TelemetryRejectReasons.ForbiddenParam, reason ?? key);
                return false;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    var text = value.GetString();

                    // Values are bounded too, not just the whole bag. A single 2KB string would
                    // otherwise pass the count check and consume the entire row budget on its own.
                    if (text is { Length: > 128 }) text = text[..128];
                    scrubbed[key] = text;
                    break;

                case JsonValueKind.Number:
                    if (value.TryGetInt64(out var whole)) scrubbed[key] = whole;
                    else if (value.TryGetDouble(out var real)) scrubbed[key] = real;
                    else scrubbed[key] = value.GetRawText();
                    break;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    scrubbed[key] = value.GetBoolean();
                    break;

                case JsonValueKind.Null:
                    // Dropped. A null parameter is the absence of a value, and storing it as one
                    // makes every consumer downstream decide separately what it meant.
                    break;

                default:
                    // Objects and arrays. A bag that can nest is a schema nobody declared, and it
                    // is what turns an event stream into an unqueryable pile by year three.
                    failure = (TelemetryRejectReasons.PayloadTooLarge,
                        $"parameter '{key}' is not a scalar");
                    return false;
            }
        }

        json = JsonSerializer.Serialize(scrubbed);

        if (Encoding.UTF8.GetByteCount(json) > _options.MaxParamsBytes)
        {
            failure = (TelemetryRejectReasons.PayloadTooLarge,
                $"parameters exceed {_options.MaxParamsBytes} bytes");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The sampling rates for the names this batch carried, so the client can turn a chatty event
    /// down on its very next batch rather than at its next release.
    /// <para>
    /// Only rates below 1.0 are sent. The common case is an empty object, and repeating "send
    /// everything" for forty names on every response is most of the payload for no information.
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, double> BuildSampling(
        IEnumerable<TelemetryEventDto> events,
        IReadOnlyDictionary<string, TelemetryEventSchema> schemas)
    {
        Dictionary<string, double>? sampling = null;

        foreach (var dto in events)
        {
            if (!schemas.TryGetValue(dto.Name, out var schema)) continue;
            if (schema.SampleRate >= 1.0) continue;

            sampling ??= new Dictionary<string, double>(StringComparer.Ordinal);
            sampling[schema.Name] = schema.SampleRate;
        }

        return sampling ?? (IReadOnlyDictionary<string, double>)new Dictionary<string, double>();
    }

    private static TelemetryRejectionDto Reject(TelemetryEventDto dto, string reason, string? detail) =>
        new() { Id = dto.Id, Name = dto.Name ?? string.Empty, Reason = reason, Detail = detail };

    private static string Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}
