using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Guidance.Interfaces;
using Share7.Application.Guidance.Models;
using Share7.Domain.Guidance;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Guidance;

public sealed class GuidanceStateService : IGuidanceStateService
{
    private readonly ApplicationDbContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GuidanceStateService(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ServiceResult<GuidanceStateResponseDto>> GetStateAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var record = await _context.UserGuidanceStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        if (record == null)
        {
            return ServiceResult<GuidanceStateResponseDto>.Success(new GuidanceStateResponseDto
            {
                Generation = 1,
                Snapshot = new GuidanceStateSnapshot()
            });
        }

        GuidanceStateSnapshot snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<GuidanceStateSnapshot>(record.StateJson, JsonOptions)
                       ?? new GuidanceStateSnapshot();
        }
        catch
        {
            snapshot = new GuidanceStateSnapshot();
        }

        return ServiceResult<GuidanceStateResponseDto>.Success(new GuidanceStateResponseDto
        {
            Generation = record.Generation,
            Snapshot = snapshot
        });
    }

    public async Task<ServiceResult<GuidanceStateResponseDto>> PushStateAsync(
        Guid userId, GuidanceStateSnapshot incoming, CancellationToken cancellationToken = default)
    {
        incoming = incoming?.Normalized() ?? new GuidanceStateSnapshot();

        var record = await _context.UserGuidanceStates
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        GuidanceStateSnapshot resultSnapshot;
        int generation;

        if (record == null)
        {
            resultSnapshot = incoming;
            generation = 1;

            record = new UserGuidanceState
            {
                UserId = userId,
                Generation = 1,
                SchemaVersion = incoming.schemaVersion,
                SessionOrdinal = incoming.sessionOrdinal,
                LastSessionDayUtc = incoming.lastSessionDayUtc ?? string.Empty,
                CompletedOnboarding = incoming.flows.Any(f => string.Equals(f.id, "onboarding.home", StringComparison.Ordinal) && f.finished),
                TotalFlowsCompleted = incoming.flows.Count(f => f.finished),
                StateJson = JsonSerializer.Serialize(incoming, JsonOptions),
                UpdatedAtUtc = DateTime.UtcNow
            };

            _context.UserGuidanceStates.Add(record);
        }
        else
        {
            generation = record.Generation;

            GuidanceStateSnapshot current;
            try
            {
                current = JsonSerializer.Deserialize<GuidanceStateSnapshot>(record.StateJson, JsonOptions)
                          ?? new GuidanceStateSnapshot();
            }
            catch
            {
                current = new GuidanceStateSnapshot();
            }

            resultSnapshot = GuidanceStateMerge.Combine(current, incoming);

            record.SchemaVersion = resultSnapshot.schemaVersion;
            record.SessionOrdinal = resultSnapshot.sessionOrdinal;
            record.LastSessionDayUtc = resultSnapshot.lastSessionDayUtc ?? string.Empty;
            record.CompletedOnboarding = resultSnapshot.flows.Any(f => string.Equals(f.id, "onboarding.home", StringComparison.Ordinal) && f.finished);
            record.TotalFlowsCompleted = resultSnapshot.flows.Count(f => f.finished);
            record.StateJson = JsonSerializer.Serialize(resultSnapshot, JsonOptions);
            record.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ServiceResult<GuidanceStateResponseDto>.Success(new GuidanceStateResponseDto
        {
            Generation = generation,
            Snapshot = resultSnapshot
        });
    }
}
