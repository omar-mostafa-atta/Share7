using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Application.Runs.Interfaces;
using Share7.Application.Runs.Models;
using Share7.Application.Runs.Models.Admin;
using Share7.Domain.Runs;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Runs;

/// <summary>
/// The admin half of the run economy: authoring prices, and reading the runs that tripped a bound.
/// <para>
/// Together these are what "economy tunable with no deploy" means. Without the first, retuning a coin
/// is a SQL script; without the second, "flagged for review" is a column nobody reads.
/// </para>
/// </summary>
public class RunAdminService : IRunAdminService
{
    private readonly ApplicationDbContext _dbContext;

    public RunAdminService(ApplicationDbContext dbContext) => _dbContext = dbContext;

    // ------------------------------------------------------------- valuations

    public async Task<IReadOnlyList<PickupValuationDto>> GetValuationsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.PickupValuations
            .AsNoTracking()
            .Include(v => v.Currency)
            .OrderBy(v => v.PickupKind)
            .ThenBy(v => v.GameId.HasValue)
            .ToListAsync(cancellationToken);

        var gameIds = rows.Where(r => r.GameId.HasValue).Select(r => r.GameId!.Value).Distinct().ToList();

        var gameKeys = await _dbContext.Games
            .AsNoTracking()
            .Where(g => gameIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.GameKey, cancellationToken);

        return rows.Select(row => ToDto(row, row.GameId is { } id ? gameKeys.GetValueOrDefault(id) : null)).ToList();
    }

    public async Task<ServiceResult<PickupValuationDto>> CreateValuationAsync(
        CreatePickupValuationRequest request,
        CancellationToken cancellationToken = default)
    {
        var kind = PickupKinds.Normalise(request.PickupKind);

        if (kind is null)
            return Invalid(
                $"'{request.PickupKind}' is not a pickup kind. Use lowercase letters, digits and underscores, starting with a letter.");

        var currency = await _dbContext.Currencies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CurrencyId, cancellationToken);

        if (currency is null)
            return ServiceResult<PickupValuationDto>.Failure(
                ApiErrors.CurrencyNotFound,
                ServiceErrorKind.NotFound,
                $"Currency {request.CurrencyId} does not exist.");

        // **The phase-5 rule, and the reason it lives at authoring time.** A hard currency is one
        // people paid real money for: an unbounded gameplay source for it is a fraud surface, it
        // destroys the price anchor, and unlike a soft currency it can never be rebalanced downward
        // afterwards. Refusing the row is the only point at which that is still cheap.
        if (currency.IsHard && request.MaxPerDay is null)
            return Invalid(
                $"'{currency.Key}' is a hard currency, so a valuation for it must set maxPerDay. An unbounded gameplay source for a currency people pay for cannot be corrected after the fact.");

        if (request.GameId is { } gameId
            && !await _dbContext.Games.AnyAsync(g => g.Id == gameId, cancellationToken))
            return ServiceResult<PickupValuationDto>.Failure(
                ApiErrors.GameNotFound,
                ServiceErrorKind.NotFound,
                $"Game {gameId} does not exist.");

        var now = DateTime.UtcNow;

        var valuation = new PickupValuation
        {
            Id = Guid.NewGuid(),
            GameId = request.GameId,
            PickupKind = kind,
            CurrencyId = request.CurrencyId,
            UnitValue = request.UnitValue,
            MaxPerRun = request.MaxPerRun,
            MaxPerDay = request.MaxPerDay,
            Enabled = request.Enabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.PickupValuations.Add(valuation);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Raised from the unique index rather than a preceding lookup, so two admins saving the
            // same price at once cannot both succeed and silently double a payout.
            _dbContext.Entry(valuation).State = EntityState.Detached;

            return ServiceResult<PickupValuationDto>.Failure(
                ApiErrors.ValuationDuplicate,
                ServiceErrorKind.Conflict,
                $"'{kind}' is already priced in '{currency.Key}' for that game. Update the existing row instead.");
        }

        return ServiceResult<PickupValuationDto>.Success(await ReadAsync(valuation.Id, cancellationToken));
    }

    public async Task<ServiceResult<PickupValuationDto>> UpdateValuationAsync(
        Guid valuationId,
        UpdatePickupValuationRequest request,
        CancellationToken cancellationToken = default)
    {
        var valuation = await _dbContext.PickupValuations
            .Include(v => v.Currency)
            .FirstOrDefaultAsync(v => v.Id == valuationId, cancellationToken);

        if (valuation is null)
            return ServiceResult<PickupValuationDto>.Failure(
                ApiErrors.ValuationNotFound,
                ServiceErrorKind.NotFound,
                $"Valuation {valuationId} does not exist.");

        // The same rule on the way through, not only on the way in. Clearing maxPerDay on a hard
        // currency's row is exactly as unbounded as never setting it.
        if (valuation.Currency is { IsHard: true } && request.MaxPerDay is null)
            return Invalid(
                $"'{valuation.Currency.Key}' is a hard currency, so its valuation must keep a maxPerDay.");

        valuation.UnitValue = request.UnitValue;
        valuation.MaxPerRun = request.MaxPerRun;
        valuation.MaxPerDay = request.MaxPerDay;
        valuation.Enabled = request.Enabled;
        valuation.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<PickupValuationDto>.Success(await ReadAsync(valuationId, cancellationToken));
    }

    // ------------------------------------------------------------- review queue

    public async Task<IReadOnlyList<RunAdminDto>> GetFlaggedRunsAsync(
        int take = 50,
        bool includeReviewed = false,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Runs
            .AsNoTracking()
            .Include(r => r.Payouts)
            .ThenInclude(p => p.Currency)
            .Where(r => r.IsFlagged);

        if (!includeReviewed)
            query = query.Where(r => r.ReviewedAtUtc == null);

        var runs = await query
            .OrderByDescending(r => r.EndedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

        return runs.Select(ToDto).ToList();
    }

    public async Task<ServiceResult<RunAdminDto>> GetRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.Runs
            .AsNoTracking()
            .Include(r => r.Payouts)
            .ThenInclude(p => p.Currency)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        return run is null
            ? ServiceResult<RunAdminDto>.Failure(
                ApiErrors.RunNotFound, ServiceErrorKind.NotFound, $"Run {runId} does not exist.")
            : ServiceResult<RunAdminDto>.Success(ToDto(run));
    }

    public async Task<ServiceResult<RunAdminDto>> ReviewRunAsync(
        Guid runId,
        Guid reviewerUserId,
        ReviewRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.Runs
            .Include(r => r.Payouts)
            .ThenInclude(p => p.Currency)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run is null)
            return ServiceResult<RunAdminDto>.Failure(
                ApiErrors.RunNotFound, ServiceErrorKind.NotFound, $"Run {runId} does not exist.");

        // IsFlagged and FlagReason are deliberately untouched. They record what happened to the run;
        // a review records a judgement about it. Clearing the flag would make the queue tidy at the
        // cost of the payout no longer being explicable, which is the one thing it must stay.
        run.ReviewedAtUtc = DateTime.UtcNow;
        run.ReviewedByUserId = reviewerUserId;
        run.ReviewNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ServiceResult<RunAdminDto>.Success(ToDto(run));
    }

    // ------------------------------------------------------------- mapping

    private async Task<PickupValuationDto> ReadAsync(Guid valuationId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.PickupValuations
            .AsNoTracking()
            .Include(v => v.Currency)
            .FirstAsync(v => v.Id == valuationId, cancellationToken);

        var gameKey = row.GameId is { } gameId
            ? await _dbContext.Games.AsNoTracking()
                .Where(g => g.Id == gameId).Select(g => g.GameKey).FirstOrDefaultAsync(cancellationToken)
            : null;

        return ToDto(row, gameKey);
    }

    private static PickupValuationDto ToDto(PickupValuation valuation, string? gameKey) => new()
    {
        Id = valuation.Id,
        GameId = valuation.GameId,
        GameKey = gameKey,
        PickupKind = valuation.PickupKind,
        CurrencyId = valuation.CurrencyId,
        Currency = valuation.Currency?.Key ?? string.Empty,
        CurrencyIsHard = valuation.Currency?.IsHard ?? false,
        CurrencyEnabled = valuation.Currency?.Enabled ?? false,
        UnitValue = valuation.UnitValue,
        MaxPerRun = valuation.MaxPerRun,
        MaxPerDay = valuation.MaxPerDay,
        Enabled = valuation.Enabled,
        CreatedAtUtc = valuation.CreatedAtUtc,
        UpdatedAtUtc = valuation.UpdatedAtUtc
    };

    private static RunAdminDto ToDto(Run run) => new()
    {
        RunId = run.Id,
        UserId = run.UserId,
        GameId = run.GameId,
        State = WireEnum.ToWire(run.State),
        Outcome = WireEnum.ToWire(run.Outcome),
        StartedAtUtc = run.StartedAtUtc,
        EndedAtUtc = run.EndedAtUtc,
        DurationMs = run.DurationMs,
        Seed = run.Seed,
        LayoutVersion = run.LayoutVersion,
        SessionId = run.SessionId,
        IsFlagged = run.IsFlagged,
        FlagReason = run.FlagReason,
        CapReached = run.CapReached,
        CapMessage = run.CapMessage,
        Collected = ParseCollected(run.PickupsJson),
        Payouts = run.Payouts
            .OrderBy(p => p.Source, StringComparer.Ordinal)
            .Select(p => new RunPayoutDto
            {
                Source = p.Source,
                Currency = p.Currency?.Key ?? string.Empty,
                CollectedCount = p.CollectedCount,
                PaidCount = p.PaidCount,
                UnitValue = p.UnitValue,
                GrossAmount = p.GrossAmount,
                CappedAmount = p.CappedAmount,
                NetAmount = p.NetAmount
            })
            .ToList(),
        ReviewedAtUtc = run.ReviewedAtUtc,
        ReviewedByUserId = run.ReviewedByUserId,
        ReviewNote = run.ReviewNote
    };

    /// <summary>
    /// Tolerant on purpose. This is a diagnostic view of a stored claim, and a row written by a
    /// deployment whose shape has since moved must still be readable enough to review — returning
    /// nothing beats throwing on the one screen somebody opens to work out what went wrong.
    /// </summary>
    private static IReadOnlyList<RunCollectedDto> ParseCollected(string pickupsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<StoredPickup>>(pickupsJson, RunJson.Options)?
                .Select(p => new RunCollectedDto { Kind = p.Kind, Count = p.Count })
                .ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record StoredPickup(string Kind, int Count);

    private static ServiceResult<PickupValuationDto> Invalid(string message) =>
        ServiceResult<PickupValuationDto>.Failure(
            ApiErrors.ValuationInvalid, ServiceErrorKind.Validation, message);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
