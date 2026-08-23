using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// Board authoring.
/// <para>
/// **Everything questionable is refused here rather than discovered later.** A board that ranks a
/// metric nothing raises, or slices by a cohort the schema cannot resolve, does not fail — it just
/// stays empty forever, and an empty board is indistinguishable from an unpopular one. By the time
/// anybody investigates, the operator who created it has moved on. The same reasoning the reward
/// engine applies to rules that could never pay.
/// </para>
/// </summary>
public class LeaderboardAdminService : ILeaderboardAdminService
{
    /// <summary>
    /// Cohorts the database can actually answer. School, class, friends and country are declared
    /// on the enum so the wire format survives their arrival, and refused until there is an
    /// enrolment relation, a social graph and a country to resolve them from.
    /// </summary>
    private static readonly HashSet<LeaderboardCohort> ResolvableCohorts =
        [LeaderboardCohort.All, LeaderboardCohort.Grade];

    private readonly ApplicationDbContext _dbContext;
    private readonly ILeaderboardProjector _projector;
    private readonly ILeaderboardRolloverService _rollover;

    public LeaderboardAdminService(
        ApplicationDbContext dbContext,
        ILeaderboardProjector projector,
        ILeaderboardRolloverService rollover)
    {
        _dbContext = dbContext;
        _projector = projector;
        _rollover = rollover;
    }

    public async Task<ServiceResult<IReadOnlyList<LeaderboardBoardAdminDto>>> GetBoardsAsync(
        CancellationToken cancellationToken = default)
    {
        var boards = await _dbContext.LeaderboardBoards
            .AsNoTracking()
            .Include(b => b.Translations)
            .Include(b => b.Cycles)
            .OrderBy(b => b.BoardKey)
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<LeaderboardBoardAdminDto>>.Success(
            boards.Select(ToDto).ToList());
    }

    public async Task<ServiceResult<LeaderboardBoardAdminDto>> CreateBoardAsync(
        SaveLeaderboardBoardRequest request, CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);

        if (validation is not null)
            return validation;

        var key = request.BoardKey.Trim().ToLowerInvariant();

        if (await _dbContext.LeaderboardBoards.AnyAsync(b => b.BoardKey == key, cancellationToken))
        {
            return ServiceResult<LeaderboardBoardAdminDto>.Failure(
                ApiErrors.LeaderboardBoardKeyTaken, ServiceErrorKind.Conflict,
                $"A board already uses the key '{key}'.");
        }

        var now = DateTime.UtcNow;

        var board = new LeaderboardBoard
        {
            Id = Guid.NewGuid(),
            BoardKey = key,
            Metric = request.Metric.Trim().ToUpperInvariant(),
            SortDirection = Enum.Parse<LeaderboardSortDirection>(request.SortDirection, true),
            Aggregation = Enum.Parse<LeaderboardAggregation>(request.Aggregation, true),
            Period = Enum.Parse<LeaderboardPeriod>(request.Period, true),
            SupportedCohorts = NormaliseCohorts(request.SupportedCohorts),
            GameId = request.GameId,
            GradeId = request.GradeId,
            LangId = request.LangId,
            VisibleRankLimit = request.VisibleRankLimit,
            GraceSeconds = request.GraceSeconds,
            IsActive = request.IsActive,
            CreatedAtUtc = now
        };

        foreach (var translation in request.Translations)
        {
            board.Translations.Add(new LeaderboardBoardTranslation
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                LangId = translation.LangId,
                Name = translation.Name.Trim(),
                Description = translation.Description?.Trim()
            });
        }

        _dbContext.LeaderboardBoards.Add(board);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // A board with no window ranks nothing, and an operator who has just created one expects
        // to see it working. Derived periods get their current window immediately; an event board
        // waits for its bounds to be authored.
        await _rollover.RolloverAsync(cancellationToken);

        return await ReadAsync(board.Id, cancellationToken);
    }

    public async Task<ServiceResult<LeaderboardBoardAdminDto>> UpdateBoardAsync(
        Guid boardId, SaveLeaderboardBoardRequest request, CancellationToken cancellationToken = default)
    {
        var board = await _dbContext.LeaderboardBoards
            .Include(b => b.Translations)
            .FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);

        if (board is null)
        {
            return ServiceResult<LeaderboardBoardAdminDto>.Failure(
                ApiErrors.LeaderboardBoardNotFound, ServiceErrorKind.NotFound, "No such leaderboard.");
        }

        var validation = Validate(request);

        if (validation is not null)
            return validation;

        // BoardKey, Metric and Aggregation are deliberately not editable. Every one of them would
        // silently change what the existing entries mean: settlements already reference the key,
        // and a board that was summing yesterday and taking the best today has a column of numbers
        // that cannot be explained by any single rule. Retire it and author a replacement.
        board.SortDirection = Enum.Parse<LeaderboardSortDirection>(request.SortDirection, true);
        board.SupportedCohorts = NormaliseCohorts(request.SupportedCohorts);
        board.VisibleRankLimit = request.VisibleRankLimit;
        board.GraceSeconds = request.GraceSeconds;
        board.IsActive = request.IsActive;
        board.UpdatedAtUtc = DateTime.UtcNow;

        _dbContext.LeaderboardBoardTranslations.RemoveRange(board.Translations);

        foreach (var translation in request.Translations)
        {
            board.Translations.Add(new LeaderboardBoardTranslation
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                LangId = translation.LangId,
                Name = translation.Name.Trim(),
                Description = translation.Description?.Trim()
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await ReadAsync(board.Id, cancellationToken);
    }

    public async Task<ServiceResult> CreateEventCycleAsync(
        Guid boardId, CreateLeaderboardCycleRequest request, CancellationToken cancellationToken = default)
    {
        var board = await _dbContext.LeaderboardBoards
            .FirstOrDefaultAsync(b => b.Id == boardId, cancellationToken);

        if (board is null)
        {
            return ServiceResult.Failure(
                ApiErrors.LeaderboardBoardNotFound, ServiceErrorKind.NotFound, "No such leaderboard.");
        }

        if (request.EndsAtUtc <= request.StartsAtUtc)
        {
            return ServiceResult.Failure(
                ApiErrors.LeaderboardBoardInvalid, ServiceErrorKind.Validation,
                "A cycle has to end after it starts.");
        }

        var now = DateTime.UtcNow;

        _dbContext.LeaderboardCycles.Add(new LeaderboardCycle
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            StartsAtUtc = request.StartsAtUtc,
            EndsAtUtc = request.EndsAtUtc,
            State = request.StartsAtUtc <= now
                ? LeaderboardCycleState.Open
                : LeaderboardCycleState.Scheduled,
            CreatedAtUtc = now
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique window index. Two cycles of one board starting at the same instant would
            // split its ranking in half with no way to tell which half is real.
            return ServiceResult.Failure(
                ApiErrors.LeaderboardBoardInvalid, ServiceErrorKind.Conflict,
                "This board already has a cycle starting at that moment.");
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RebuildCycleAsync(
        Guid cycleId, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.LeaderboardCycles.AnyAsync(c => c.Id == cycleId, cancellationToken))
        {
            return ServiceResult.Failure(
                ApiErrors.LeaderboardCycleNotFound, ServiceErrorKind.NotFound, "No such cycle.");
        }

        await _projector.RebuildCycleAsync(cycleId, cancellationToken);
        await _projector.ProjectPendingAsync(int.MaxValue, cancellationToken);
        await _projector.ReindexCycleAsync(cycleId, cancellationToken);

        return ServiceResult.Success();
    }

    // ------------------------------------------------------------- validation

    /// <summary>
    /// Returns a refusal, or null when the board is authorable. Everything checked here is
    /// something that would otherwise produce a board that quietly never fills.
    /// </summary>
    private static ServiceResult<LeaderboardBoardAdminDto>? Validate(SaveLeaderboardBoardRequest request)
    {
        var metric = request.Metric?.Trim().ToUpperInvariant();

        if (!LeaderboardMetrics.IsKnown(metric))
        {
            return Invalid(
                $"Nothing raises the metric '{request.Metric}'. Known metrics: " +
                $"{string.Join(", ", LeaderboardMetrics.Known)}.");
        }

        if (!Enum.TryParse<LeaderboardSortDirection>(request.SortDirection, true, out _))
            return Invalid($"'{request.SortDirection}' is not a sort direction.");

        if (!Enum.TryParse<LeaderboardAggregation>(request.Aggregation, true, out _))
            return Invalid($"'{request.Aggregation}' is not an aggregation.");

        if (!Enum.TryParse<LeaderboardPeriod>(request.Period, true, out _))
            return Invalid($"'{request.Period}' is not a period.");

        var cohorts = (request.SupportedCohorts ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (cohorts.Count == 0)
            return Invalid("A board has to offer at least one cohort.");

        foreach (var name in cohorts)
        {
            if (!Enum.TryParse<LeaderboardCohort>(name, true, out var cohort))
                return Invalid($"'{name}' is not a cohort.");

            if (!ResolvableCohorts.Contains(cohort))
            {
                return Invalid(
                    $"The {cohort} cohort cannot be resolved yet — the schema has no relation to " +
                    "answer it. Author this board with All or Grade and add the cohort when it lands.");
            }
        }

        if (request.Translations.GroupBy(t => t.LangId).Any(group => group.Count() > 1))
            return Invalid("A board can only have one name per language.");

        return null;
    }

    private static ServiceResult<LeaderboardBoardAdminDto> Invalid(string message) =>
        ServiceResult<LeaderboardBoardAdminDto>.Failure(
            ApiErrors.LeaderboardBoardInvalid, ServiceErrorKind.Validation, message);

    private static string NormaliseCohorts(string? raw) =>
        string.Join(',', (raw ?? "All")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => Enum.Parse<LeaderboardCohort>(name, true).ToString())
            .Distinct());

    private async Task<ServiceResult<LeaderboardBoardAdminDto>> ReadAsync(
        Guid boardId, CancellationToken cancellationToken)
    {
        var board = await _dbContext.LeaderboardBoards
            .AsNoTracking()
            .Include(b => b.Translations)
            .Include(b => b.Cycles)
            .FirstAsync(b => b.Id == boardId, cancellationToken);

        return ServiceResult<LeaderboardBoardAdminDto>.Success(ToDto(board));
    }

    private static LeaderboardBoardAdminDto ToDto(LeaderboardBoard board) => new()
    {
        BoardId = board.Id,
        BoardKey = board.BoardKey,
        Metric = board.Metric,
        SortDirection = board.SortDirection.ToString(),
        Aggregation = board.Aggregation.ToString(),
        Period = board.Period.ToString(),
        SupportedCohorts = board.SupportedCohorts,
        GameId = board.GameId,
        GradeId = board.GradeId,
        LangId = board.LangId,
        VisibleRankLimit = board.VisibleRankLimit,
        GraceSeconds = board.GraceSeconds,
        IsActive = board.IsActive,
        CycleCount = board.Cycles.Count,
        Translations = board.Translations
            .Select(t => new LeaderboardBoardTranslationRequest
            {
                LangId = t.LangId,
                Name = t.Name,
                Description = t.Description
            })
            .ToList()
    };
}
