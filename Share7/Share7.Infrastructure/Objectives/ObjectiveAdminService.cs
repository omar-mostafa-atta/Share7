using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Objectives.Interfaces;
using Share7.Application.Objectives.Models;
using Share7.Domain.Leaderboards;
using Share7.Domain.Objectives;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Objectives;

/// <inheritdoc cref="IObjectiveAdminService"/>
public class ObjectiveAdminService : IObjectiveAdminService
{
    private readonly ApplicationDbContext _dbContext;

    public ObjectiveAdminService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlyList<ObjectiveAdminDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var objectives = await _dbContext.Objectives
            .AsNoTracking()
            .Include(o => o.Translations)
            .OrderBy(o => o.Kind)
            .ThenBy(o => o.SortOrder)
            .ToListAsync(cancellationToken);

        return objectives.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<ObjectiveAdminDto>> CreateAsync(
        CreateObjectiveRequest request, CancellationToken cancellationToken = default)
    {
        var key = request.Key.Trim();

        if (!WireEnum.TryFromWire<ObjectiveKind>(request.Kind, out var kind))
            return ServiceResult<ObjectiveAdminDto>.Invalid(
                $"'{request.Kind}' is not an objective kind.");

        if (!WireEnum.TryFromWire<LeaderboardAggregation>(request.Aggregation, out var aggregation))
            return ServiceResult<ObjectiveAdminDto>.Invalid(
                $"'{request.Aggregation}' is not an aggregation. Use SUM, BEST or LAST.");

        // The same rule boards obey, for the same reason: an objective on a metric nothing raises is
        // dead configuration an operator creates, sees no error from, and waits forever on.
        if (!LeaderboardMetrics.IsKnown(request.Metric))
            return ServiceResult<ObjectiveAdminDto>.Invalid(
                $"'{request.Metric}' is not a known metric. Nothing would ever raise it.");

        if (await _dbContext.Objectives.AnyAsync(o => o.Key == key, cancellationToken))
            return ServiceResult<ObjectiveAdminDto>.Conflict($"An objective '{key}' already exists.");

        if (DuplicateLanguage(request.Translations) is { } duplicate)
            return ServiceResult<ObjectiveAdminDto>.Invalid(
                $"Language {duplicate} is listed more than once.");

        var now = DateTime.UtcNow;

        var objective = new Objective
        {
            Id = Guid.NewGuid(),
            Key = key,
            Kind = kind,
            Metric = request.Metric.Trim(),
            Scope = string.IsNullOrWhiteSpace(request.Scope) ? null : request.Scope.Trim(),
            Target = request.Target,
            Aggregation = aggregation,
            GameId = request.GameId,
            GradeId = request.GradeId,
            LangId = request.LangId,
            AvailableFromUtc = request.AvailableFromUtc,
            AvailableToUtc = request.AvailableToUtc,
            IconKey = string.IsNullOrWhiteSpace(request.IconKey) ? null : request.IconKey.Trim(),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Translations = [.. request.Translations.Select(t => new ObjectiveTranslation
            {
                Id = Guid.NewGuid(),
                LangId = t.LangId,
                Name = t.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(t.Description) ? null : t.Description.Trim()
            })]
        };

        _dbContext.Objectives.Add(objective);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<ObjectiveAdminDto>.Success(ToDto(objective));
    }

    public async Task<ServiceResult<ObjectiveAdminDto>> UpdateAsync(
        Guid objectiveId, UpdateObjectiveRequest request, CancellationToken cancellationToken = default)
    {
        var objective = await _dbContext.Objectives
            .Include(o => o.Translations)
            .FirstOrDefaultAsync(o => o.Id == objectiveId, cancellationToken);

        if (objective is null)
            return ServiceResult<ObjectiveAdminDto>.NotFound($"No objective {objectiveId}.");

        if (DuplicateLanguage(request.Translations) is { } duplicate)
            return ServiceResult<ObjectiveAdminDto>.Invalid(
                $"Language {duplicate} is listed more than once.");

        // Key, kind, metric and scope are absent from the request on purpose — changing any of them
        // would strand every progress row counting under the old meaning, and the reward
        // transactions already paid against the old key.
        objective.Target = request.Target;
        objective.AvailableFromUtc = request.AvailableFromUtc;
        objective.AvailableToUtc = request.AvailableToUtc;
        objective.IconKey = string.IsNullOrWhiteSpace(request.IconKey) ? null : request.IconKey.Trim();
        objective.SortOrder = request.SortOrder;
        objective.IsActive = request.IsActive;
        objective.UpdatedAtUtc = DateTime.UtcNow;

        // Replaced wholesale, and the delete has to reach the database before the inserts: a
        // re-sent language would otherwise collide with the row still holding its unique index.
        _dbContext.ObjectiveTranslations.RemoveRange(objective.Translations);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var translation in request.Translations)
        {
            _dbContext.ObjectiveTranslations.Add(new ObjectiveTranslation
            {
                Id = Guid.NewGuid(),
                ObjectiveId = objective.Id,
                LangId = translation.LangId,
                Name = translation.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(translation.Description)
                    ? null
                    : translation.Description.Trim()
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var saved = await _dbContext.Objectives
            .AsNoTracking()
            .Include(o => o.Translations)
            .FirstAsync(o => o.Id == objectiveId, cancellationToken);

        return ServiceResult<ObjectiveAdminDto>.Success(ToDto(saved));
    }

    private static Guid? DuplicateLanguage(IEnumerable<ObjectiveTranslationRequest> translations) =>
        translations
            .GroupBy(t => t.LangId)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

    private static ObjectiveAdminDto ToDto(Objective objective) => new()
    {
        ObjectiveId = objective.Id,
        Key = objective.Key,
        Kind = WireEnum.ToWire(objective.Kind),
        Metric = objective.Metric,
        Scope = objective.Scope,
        Target = objective.Target,
        Aggregation = WireEnum.ToWire(objective.Aggregation),
        GameId = objective.GameId,
        GradeId = objective.GradeId,
        LangId = objective.LangId,
        AvailableFromUtc = objective.AvailableFromUtc,
        AvailableToUtc = objective.AvailableToUtc,
        IconKey = objective.IconKey,
        SortOrder = objective.SortOrder,
        IsActive = objective.IsActive,
        Translations = [.. objective.Translations.Select(t => new ObjectiveTranslationRequest
        {
            LangId = t.LangId,
            Name = t.Name,
            Description = t.Description
        })]
    };
}
