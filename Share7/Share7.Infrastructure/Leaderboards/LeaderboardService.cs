using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Domain.Leaderboards;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// The read side. Every query here is an index seek against a materialised rank — there is no
/// sorting, no counting and no window function on the request path, because the deployment has no
/// Redis and a shared CPU budget.
/// <para>
/// **Cohort is resolved from the caller's identity, never from the request.** The client sends the
/// *kind* of cohort it wants (<c>grade</c>); it never sends the value. A caller who could name the
/// value could rank themselves inside a cohort they do not belong to, which on a school
/// leaderboard is not a hypothetical.
/// </para>
/// </summary>
public class LeaderboardService : ILeaderboardService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDisplayNameService _displayNames;
    private readonly ILanguageService _languageService;
    private readonly LeaderboardOptions _options;
    private readonly byte[] _cursorKey;

    public LeaderboardService(
        ApplicationDbContext dbContext,
        IDisplayNameService displayNames,
        ILanguageService languageService,
        IOptions<LeaderboardOptions> options,
        IOptions<JwtSettings> jwtSettings)
    {
        _dbContext = dbContext;
        _displayNames = displayNames;
        _languageService = languageService;
        _options = options.Value;

        // Signed with the deployment's existing secret rather than a new one. A second key to
        // configure is a second key to leave at its default, and the cursor lives in the same
        // trust domain as the token that authorised the request carrying it.
        _cursorKey = Encoding.UTF8.GetBytes(jwtSettings.Value.Secret);
    }

    // ------------------------------------------------------------- listings

    public async Task<ServiceResult<IReadOnlyList<LeaderboardBoardDto>>> GetBoardsAsync(
        Guid userId, Guid? gameId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Disabled<IReadOnlyList<LeaderboardBoardDto>>();

        var langId = await _languageService.ResolveCurrentAsync(cancellationToken);
        var callerGrade = await GradeOfAsync(userId, cancellationToken);

        var boards = await _dbContext.LeaderboardBoards
            .AsNoTracking()
            .Where(b => b.IsActive
                        && (gameId == null || b.GameId == gameId)
                        && (b.LangId == null || b.LangId == langId))
            .Include(b => b.Translations.Where(t => t.LangId == langId))
            .ToListAsync(cancellationToken);

        var boardIds = boards.Select(b => b.Id).ToList();

        // One query for every board's live cycle rather than one per board. A listing that issued
        // a query per row would be the slowest screen in the app the moment a dozen boards exist.
        var cycles = await _dbContext.LeaderboardCycles
            .AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId)
                        && (c.State == LeaderboardCycleState.Open
                            || c.State == LeaderboardCycleState.Scheduled))
            .OrderBy(c => c.StartsAtUtc)
            .ToListAsync(cancellationToken);

        var liveByBoard = cycles
            .GroupBy(c => c.BoardId)
            .ToDictionary(
                group => group.Key,
                group => group.FirstOrDefault(c => c.State == LeaderboardCycleState.Open)
                         ?? group.First());

        var dtos = boards
            .Select(board => new LeaderboardBoardDto
            {
                BoardId = board.Id,
                BoardKey = board.BoardKey,
                Name = board.Translations.FirstOrDefault()?.Name ?? board.BoardKey,
                Description = board.Translations.FirstOrDefault()?.Description,
                Metric = board.Metric,
                SortDirection = board.SortDirection.ToString(),
                Period = board.Period.ToString(),
                GameId = board.GameId,
                SupportedCohorts = AvailableCohorts(board, callerGrade),
                CurrentCycle = liveByBoard.TryGetValue(board.Id, out var cycle) ? ToDto(cycle) : null
            })
            .ToList();

        return ServiceResult<IReadOnlyList<LeaderboardBoardDto>>.Success(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<LeaderboardCycleDto>>> GetCyclesAsync(
        Guid boardId, int limit, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Disabled<IReadOnlyList<LeaderboardCycleDto>>();

        if (!await _dbContext.LeaderboardBoards.AnyAsync(b => b.Id == boardId, cancellationToken))
            return NotFoundBoard<IReadOnlyList<LeaderboardCycleDto>>();

        var cycles = await _dbContext.LeaderboardCycles
            .AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .OrderByDescending(c => c.StartsAtUtc)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<LeaderboardCycleDto>>.Success(
            cycles.Select(ToDto).ToList());
    }

    // ------------------------------------------------------------- the page

    public async Task<ServiceResult<LeaderboardPageDto>> GetPageAsync(
        Guid userId, Guid cycleId, string? cohort, string? cursor, int? limit,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Disabled<LeaderboardPageDto>();

        var resolved = await ResolveAsync(userId, cycleId, cohort, cancellationToken);

        if (!resolved.Succeeded)
            return ServiceResult<LeaderboardPageDto>.Failure(
                resolved.Error!, resolved.ErrorKind, resolved.Errors.FirstOrDefault() ?? "Refused.");

        var (cycle, cohortKind, cohortKey) = resolved.Value!;

        var pageSize = Math.Clamp(limit ?? _options.DefaultPageSize, 1, _options.MaxPageSize);

        var afterRank = 0;

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!LeaderboardCursor.TryDecode(cursor, _cursorKey, out var decoded))
            {
                return ServiceResult<LeaderboardPageDto>.Failure(
                    ApiErrors.LeaderboardCursorInvalid, ServiceErrorKind.Validation,
                    "That paging cursor is not valid. Start from the top of the board.");
            }

            // A cursor minted for another board is as invalid as a forged one — otherwise a stale
            // deep link would silently page through the wrong leaderboard.
            if (decoded.CycleId != cycleId || decoded.Cohort != (int)cohortKind || decoded.CohortKey != cohortKey)
            {
                return ServiceResult<LeaderboardPageDto>.Failure(
                    ApiErrors.LeaderboardCursorInvalid, ServiceErrorKind.Validation,
                    "That paging cursor belongs to a different board.");
            }

            afterRank = decoded.AfterRank;
        }

        var limitRank = cycle.Board?.VisibleRankLimit;

        if (limitRank is { } cap && afterRank >= cap)
        {
            return ServiceResult<LeaderboardPageDto>.Failure(
                ApiErrors.LeaderboardRankLimit, ServiceErrorKind.Forbidden,
                "This board does not go any deeper for your account.");
        }

        // Hidden players are filtered here and nowhere else. They keep their rank — the numbers on
        // this page will therefore have gaps, which is correct: rank 4 being absent is not rank 5
        // being promoted.
        var query = _dbContext.LeaderboardEntries
            .AsNoTracking()
            .Where(e => e.CycleId == cycleId
                        && e.Cohort == cohortKind
                        && e.CohortKey == cohortKey
                        && e.Rank > afterRank
                        && !e.IsHidden
                        && !e.IsFlagged);

        if (limitRank is { } depth)
            query = query.Where(e => e.Rank <= depth);

        var rows = await query
            .OrderBy(e => e.Rank)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;

        var truncatedAt = limitRank is { } edge && page.Count > 0 && page[^1].Rank >= edge
            ? edge
            : (int?)null;

        return ServiceResult<LeaderboardPageDto>.Success(new LeaderboardPageDto
        {
            CycleId = cycleId,
            Cohort = cohortKind.ToString(),
            State = cycle.State.ToString(),
            Entries = page.Select(e => ToDto(e, userId)).ToList(),
            NextCursor = hasMore && truncatedAt is null
                ? new LeaderboardCursor(cycleId, (int)cohortKind, cohortKey, page[^1].Rank).Encode(_cursorKey)
                : null,
            TotalRanked = cycle.TotalRanked,
            TruncatedAtRank = truncatedAt,
            ServerTimeUtc = DateTime.UtcNow
        });
    }

    public async Task<ServiceResult<LeaderboardNeighbourhoodDto>> GetAroundMeAsync(
        Guid userId, Guid cycleId, string? cohort, int? window,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Disabled<LeaderboardNeighbourhoodDto>();

        var resolved = await ResolveAsync(userId, cycleId, cohort, cancellationToken);

        if (!resolved.Succeeded)
            return ServiceResult<LeaderboardNeighbourhoodDto>.Failure(
                resolved.Error!, resolved.ErrorKind, resolved.Errors.FirstOrDefault() ?? "Refused.");

        var (cycle, cohortKind, cohortKey) = resolved.Value!;

        var span = Math.Clamp(window ?? 5, 1, 25);

        var standing = await StandingRowAsync(userId, cycleId, cohortKind, cohortKey, cancellationToken);

        // No entry is a legitimate state — "you have not played this week" — so it answers with an
        // empty neighbourhood and a null rank rather than a 404 the client has to special-case.
        if (standing is null)
        {
            return ServiceResult<LeaderboardNeighbourhoodDto>.Success(new LeaderboardNeighbourhoodDto
            {
                CycleId = cycleId,
                Cohort = cohortKind.ToString(),
                Entries = [],
                Standing = await StandingDtoAsync(userId, cycle, cohortKind, null, cancellationToken),
                ServerTimeUtc = DateTime.UtcNow
            });
        }

        var from = Math.Max(1, standing.Rank - span);
        var to = standing.Rank + span;

        var rows = await _dbContext.LeaderboardEntries
            .AsNoTracking()
            .Where(e => e.CycleId == cycleId
                        && e.Cohort == cohortKind
                        && e.CohortKey == cohortKey
                        && e.Rank >= from
                        && e.Rank <= to
                        && !e.IsFlagged
                        // The caller sees themselves even when hidden; everyone else obeys the flag.
                        && (!e.IsHidden || e.UserId == userId))
            .OrderBy(e => e.Rank)
            .ToListAsync(cancellationToken);

        return ServiceResult<LeaderboardNeighbourhoodDto>.Success(new LeaderboardNeighbourhoodDto
        {
            CycleId = cycleId,
            Cohort = cohortKind.ToString(),
            Entries = rows.Select(e => ToDto(e, userId)).ToList(),
            Standing = await StandingDtoAsync(userId, cycle, cohortKind, standing, cancellationToken),
            ServerTimeUtc = DateTime.UtcNow
        });
    }

    public async Task<ServiceResult<LeaderboardStandingDto>> GetStandingAsync(
        Guid userId, Guid cycleId, string? cohort, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Disabled<LeaderboardStandingDto>();

        var resolved = await ResolveAsync(userId, cycleId, cohort, cancellationToken);

        if (!resolved.Succeeded)
            return ServiceResult<LeaderboardStandingDto>.Failure(
                resolved.Error!, resolved.ErrorKind, resolved.Errors.FirstOrDefault() ?? "Refused.");

        var (cycle, cohortKind, cohortKey) = resolved.Value!;

        var row = await StandingRowAsync(userId, cycleId, cohortKind, cohortKey, cancellationToken);

        return ServiceResult<LeaderboardStandingDto>.Success(
            await StandingDtoAsync(userId, cycle, cohortKind, row, cancellationToken));
    }

    public async Task<ServiceResult<LeaderboardSettlementDto>> GetSettlementAsync(
        Guid userId, Guid cycleId, string? cohort, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Disabled<LeaderboardSettlementDto>();

        var resolved = await ResolveAsync(userId, cycleId, cohort, cancellationToken);

        if (!resolved.Succeeded)
            return ServiceResult<LeaderboardSettlementDto>.Failure(
                resolved.Error!, resolved.ErrorKind, resolved.Errors.FirstOrDefault() ?? "Refused.");

        var (cycle, cohortKind, cohortKey) = resolved.Value!;

        // A rank that can still move is not a result. Answering with a provisional placing would
        // let a results screen congratulate a child on a third place they are about to lose.
        if (cycle.State != LeaderboardCycleState.Settled)
        {
            return ServiceResult<LeaderboardSettlementDto>.Failure(
                ApiErrors.LeaderboardCycleNotFound, ServiceErrorKind.NotFound,
                "This cycle has not been settled yet.");
        }

        var settlement = await _dbContext.LeaderboardSettlements
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.CycleId == cycleId
                     && s.Cohort == cohortKind
                     && s.CohortKey == cohortKey
                     && s.UserId == userId,
                cancellationToken);

        if (settlement is null)
        {
            return ServiceResult<LeaderboardSettlementDto>.Failure(
                ApiErrors.LeaderboardCycleNotFound, ServiceErrorKind.NotFound,
                "You did not place in this cycle.");
        }

        return ServiceResult<LeaderboardSettlementDto>.Success(new LeaderboardSettlementDto
        {
            CycleId = cycleId,
            Cohort = cohortKind.ToString(),
            FinalRank = settlement.FinalRank,
            Value = settlement.Value,
            // The band alone, not the full "{boardKey}:{band}" scope. The client is rendering a
            // rosette, not resolving a reward rule.
            RewardBand = settlement.RewardReferenceKey?.Split(':').LastOrDefault(),
            RewardIssued = settlement.RewardIssued,
            RewardIssuedAtUtc = settlement.RewardIssuedAtUtc,
            SettledAtUtc = cycle.SettledAtUtc ?? settlement.CreatedAtUtc
        });
    }

    // ------------------------------------------------------------- visibility

    public async Task<ServiceResult<LeaderboardVisibilityDto>> GetVisibilityAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var handle = await _displayNames.EnsureHandleAsync(userId, cancellationToken);

        var row = await _dbContext.PlayerDisplayNames
            .AsNoTracking()
            .FirstAsync(n => n.UserId == userId, cancellationToken);

        return ServiceResult<LeaderboardVisibilityDto>.Success(new LeaderboardVisibilityDto
        {
            DisplayName = handle,
            IsHidden = row.IsHidden || row.IsHiddenByGuardian,
            IsLockedByGuardian = row.IsHiddenByGuardian
        });
    }

    public async Task<ServiceResult<LeaderboardVisibilityDto>> SetVisibilityAsync(
        Guid userId, bool isHidden, CancellationToken cancellationToken = default)
    {
        var applied = await _displayNames.SetHiddenAsync(userId, isHidden, cancellationToken);

        if (!applied)
        {
            return ServiceResult<LeaderboardVisibilityDto>.Failure(
                ApiErrors.Forbidden, ServiceErrorKind.Forbidden,
                "A guardian has turned off leaderboard listing for this account.");
        }

        return await GetVisibilityAsync(userId, cancellationToken);
    }

    // ------------------------------------------------------------- resolution

    /// <summary>
    /// Finds the cycle and works out which cohort instance this caller belongs to.
    /// <para>
    /// The refusals are deliberately two different codes. "This board has no class cohort" and
    /// "you are not in a class" call for different words on screen, and a client given one code
    /// for both will show the wrong one to somebody.
    /// </para>
    /// </summary>
    private async Task<ServiceResult<(LeaderboardCycle Cycle, LeaderboardCohort Kind, Guid Key)>> ResolveAsync(
        Guid userId, Guid cycleId, string? cohort, CancellationToken cancellationToken)
    {
        var cycle = await _dbContext.LeaderboardCycles
            .AsNoTracking()
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == cycleId, cancellationToken);

        if (cycle?.Board is null)
        {
            return ServiceResult<(LeaderboardCycle, LeaderboardCohort, Guid)>.Failure(
                ApiErrors.LeaderboardCycleNotFound, ServiceErrorKind.NotFound, "No such leaderboard cycle.");
        }

        var kind = LeaderboardCohort.All;

        if (!string.IsNullOrWhiteSpace(cohort)
            && !Enum.TryParse(cohort, ignoreCase: true, out kind))
        {
            return ServiceResult<(LeaderboardCycle, LeaderboardCohort, Guid)>.Failure(
                ApiErrors.LeaderboardCohortUnsupported, ServiceErrorKind.Validation,
                $"'{cohort}' is not a cohort this platform ranks by.");
        }

        if (!cycle.Board.SupportedCohorts.Contains(kind.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<(LeaderboardCycle, LeaderboardCohort, Guid)>.Failure(
                ApiErrors.LeaderboardCohortUnsupported, ServiceErrorKind.Validation,
                "This board does not offer that cohort.");
        }

        Guid key;

        switch (kind)
        {
            case LeaderboardCohort.All:
                key = Guid.Empty;
                break;

            case LeaderboardCohort.Grade:
                // Resolved from the caller's own profile. The client never sends a grade id — one
                // that could would be able to rank itself against a grade it is not in.
                var gradeId = await GradeOfAsync(userId, cancellationToken);

                if (gradeId is null)
                {
                    return ServiceResult<(LeaderboardCycle, LeaderboardCohort, Guid)>.Failure(
                        ApiErrors.LeaderboardCohortUnavailable, ServiceErrorKind.Conflict,
                        "Finish your profile to see how you compare with your grade.");
                }

                key = gradeId.Value;
                break;

            default:
                // School, class, friends and country are declared on the enum so the wire format
                // does not move when they land, and refused until the schema can answer them.
                return ServiceResult<(LeaderboardCycle, LeaderboardCohort, Guid)>.Failure(
                    ApiErrors.LeaderboardCohortUnavailable, ServiceErrorKind.Conflict,
                    $"The {kind} leaderboard is not available yet.");
        }

        return ServiceResult<(LeaderboardCycle, LeaderboardCohort, Guid)>.Success((cycle, kind, key));
    }

    private Task<Guid?> GradeOfAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.GradeId)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<LeaderboardEntry?> StandingRowAsync(
        Guid userId, Guid cycleId, LeaderboardCohort kind, Guid key, CancellationToken cancellationToken) =>
        _dbContext.LeaderboardEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.CycleId == cycleId && e.Cohort == kind && e.CohortKey == key && e.UserId == userId,
                cancellationToken);

    private async Task<LeaderboardStandingDto> StandingDtoAsync(
        Guid userId,
        LeaderboardCycle cycle,
        LeaderboardCohort kind,
        LeaderboardEntry? row,
        CancellationToken cancellationToken) => new()
    {
        CycleId = cycle.Id,
        Cohort = kind.ToString(),
        Rank = row?.Rank,
        Value = row?.Value,
        TotalRanked = cycle.TotalRanked,
        Percentile = Percentile(row?.Rank, cycle.TotalRanked),
        IsHidden = await _displayNames.IsHiddenAsync(userId, cancellationToken),
        ServerTimeUtc = DateTime.UtcNow
    };

    /// <summary>
    /// How far up the board, as a whole number. Rank 1 of 100 is 100; last place is 1, never 0 —
    /// telling a child they are in the zeroth percentile is a worse way to say the same thing.
    /// </summary>
    private static int? Percentile(int? rank, int totalRanked)
    {
        if (rank is not { } position || totalRanked <= 0)
            return null;

        var share = (totalRanked - position + 1) / (double)totalRanked;

        return Math.Clamp((int)Math.Round(share * 100, MidpointRounding.AwayFromZero), 1, 100);
    }

    private static LeaderboardEntryDto ToDto(LeaderboardEntry entry, Guid callerId) => new()
    {
        Rank = entry.Rank,
        UserId = entry.UserId,
        DisplayName = entry.DisplayName,
        AvatarKey = entry.AvatarKey,
        Value = entry.Value,
        AchievedAtUtc = entry.AchievedAtUtc,
        IsSelf = entry.UserId == callerId
    };

    private static LeaderboardCycleDto ToDto(LeaderboardCycle cycle) => new()
    {
        CycleId = cycle.Id,
        StartsAtUtc = cycle.StartsAtUtc,
        // An endless cycle reports null rather than the year 9999, so a countdown has an obvious
        // "there is no countdown" case instead of rendering four thousand years.
        EndsAtUtc = cycle.EndsAtUtc >= DateTime.MaxValue.AddDays(-1) ? null : cycle.EndsAtUtc,
        State = cycle.State.ToString(),
        TotalRanked = cycle.TotalRanked
    };

    /// <summary>
    /// Cohorts this caller can actually use, rather than the ones the board declares. A grade
    /// cohort offered to a child with no grade is a button that only ever produces an error.
    /// </summary>
    private static IReadOnlyList<string> AvailableCohorts(LeaderboardBoard board, Guid? callerGrade)
    {
        var declared = board.SupportedCohorts
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return declared
            .Where(name => Enum.TryParse<LeaderboardCohort>(name, ignoreCase: true, out var kind)
                           && kind switch
                           {
                               LeaderboardCohort.All => true,
                               LeaderboardCohort.Grade => callerGrade is not null,
                               _ => false
                           })
            .Select(name => name.Trim())
            .ToList();
    }

    private static ServiceResult<T> Disabled<T>() =>
        ServiceResult<T>.Failure(
            ApiErrors.LeaderboardDisabled, ServiceErrorKind.Conflict,
            "Leaderboards are not switched on for this deployment.");

    private static ServiceResult<T> NotFoundBoard<T>() =>
        ServiceResult<T>.Failure(
            ApiErrors.LeaderboardBoardNotFound, ServiceErrorKind.NotFound, "No such leaderboard.");
}
