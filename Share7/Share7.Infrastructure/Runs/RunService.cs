using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Curriculum.Interfaces;
using Share7.Application.Leaderboards.Interfaces;
using Share7.Application.Objectives.Interfaces;
using Share7.Application.Leaderboards.Models;
using Share7.Domain.Leaderboards;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Application.Rewards.Interfaces;
using Share7.Application.Rewards.Models;
using Share7.Application.Runs.Interfaces;
using Share7.Application.Runs.Models;
using Share7.Domain.Economy;
using Share7.Domain.Multiplayer;
using Share7.Domain.Runs;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Runs;

/// <summary>
/// Opens runs and re-values them. **This is where a 3D coin stops being currency and becomes a
/// gameplay signal.**
/// <para>
/// The client reports counts; every amount in this file comes from <see cref="PickupValuation"/> or a
/// <c>RUN_SETTLED</c> reward rule, and every balance move goes through <see cref="IWalletService"/>
/// inside one transaction with the rows explaining it. There is no path from a mini-game to a
/// balance, and no request shape in which a client could assert one.
/// </para>
/// <para>
/// **Two kinds of defence, and they answer differently.** Plausibility bounds are probabilistic — a
/// four-second run claiming 300 coins is *unlikely*, and a child with a bad clock or a resumed session
/// trips them legitimately, so they cap, flag and pay. Layout verification is exact — a track the
/// server generated either had 180 coins on it or it did not — and it is the only input allowed to
/// reject a run outright.
/// </para>
/// </summary>
public class RunService : IRunService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWalletService _wallet;
    private readonly IRewardService _rewards;
    private readonly IEarnCeilingService _ceiling;
    private readonly IRunLayoutVerifier _layouts;
    private readonly IGameResultRecorder _gameResults;
    private readonly IObjectiveProjector _objectives;
    private readonly ILanguageService _languageService;
    private readonly RunOptions _options;

    public RunService(
        ApplicationDbContext dbContext,
        IWalletService wallet,
        IRewardService rewards,
        IEarnCeilingService ceiling,
        IRunLayoutVerifier layouts,
        IGameResultRecorder gameResults,
        IObjectiveProjector objectives,
        ILanguageService languageService,
        IOptions<RunOptions> options)
    {
        _dbContext = dbContext;
        _wallet = wallet;
        _rewards = rewards;
        _ceiling = ceiling;
        _layouts = layouts;
        _gameResults = gameResults;
        _objectives = objectives;
        _languageService = languageService;
        _options = options.Value;
    }

    // ------------------------------------------------------------- starting

    public async Task<ServiceResult<StartRunResponse>> StartAsync(
        Guid userId,
        StartRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestId = Normalise(request.RequestId);

        // A retry returns the original run *and its seed*. Checked before the game lookup so a replay
        // stays cheap and stays correct even if the game has been retired since.
        if (requestId is not null)
        {
            var replay = await _dbContext.Runs
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.UserId == userId && r.StartRequestId == requestId,
                    cancellationToken);

            if (replay is not null)
                return ServiceResult<StartRunResponse>.Success(Started(replay));
        }

        var game = await _dbContext.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == request.GameId, cancellationToken);

        if (game is null)
            return ServiceResult<StartRunResponse>.Failure(
                ApiErrors.GameNotFound,
                ServiceErrorKind.NotFound,
                $"Game {request.GameId} does not exist.");

        if (!game.IsActive)
            return ServiceResult<StartRunResponse>.Failure(
                ApiErrors.GameInactive,
                ServiceErrorKind.Conflict,
                $"Game '{game.GameKey}' is retired and cannot start new runs.");

        var now = DateTime.UtcNow;

        await ExpireOldestOpenRunsAsync(userId, now, cancellationToken);

        var run = new Run
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameId = request.GameId,
            Seed = NextSeed(),
            // Stamped now, from the generator that is live at *start*. Re-reading it at settlement
            // would verify a client's track against a generator it never used.
            LayoutVersion = _layouts.VersionFor(game.GameKey),
            SessionId = request.SessionId,
            StartedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.RunLifetimeMinutes),
            State = RunState.Open,
            Outcome = RunOutcome.Unknown,
            StartRequestId = requestId,
            PickupsJson = "[]"
        };

        _dbContext.Runs.Add(run);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        // The run points at a user that is no longer there. An access token outlives the account it
        // names — deleting an account cannot reach into a device and expire one — so a child who
        // deletes their account and whose device starts one more run lands exactly here. Refused as
        // a missing account rather than thrown, because a 500 on the way out of a deletion flow is
        // the worst possible moment to look broken.
        catch (DbUpdateException exception) when (IsForeignKeyViolation(exception))
        {
            Detach(run);

            return ServiceResult<StartRunResponse>.Failure(
                ApiErrors.AccountNotFound,
                ServiceErrorKind.NotFound,
                "The account this token was issued for no longer exists.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Two starts with one key raced past the read above. The index picked a winner; this is
            // the loser, and it is owed the winner's answer rather than an error — the client cannot
            // tell the two calls apart and must not end up with two tracks.
            Detach(run);

            var winner = await _dbContext.Runs
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.UserId == userId && r.StartRequestId == requestId,
                    cancellationToken);

            if (winner is null)
                throw;

            return ServiceResult<StartRunResponse>.Success(Started(winner));
        }

        return ServiceResult<StartRunResponse>.Success(Started(run));
    }

    /// <summary>
    /// Keeps at most <see cref="RunOptions.MaxConcurrentOpenRuns"/> - 1 runs open, so the one about to
    /// be created fits.
    /// <para>
    /// **Expires the oldest rather than refusing the new one.** The child in front of the device is
    /// trying to play now; the run they abandoned twenty minutes ago is the one that should give way.
    /// Refusing instead would turn a client that leaks open runs into a player who cannot start a game.
    /// </para>
    /// <para>
    /// The cap exists because every other bound here is per-run: without it a client opens ten thousand
    /// runs and settles them as a batch, and nothing per-run ever sees a farming pattern.
    /// </para>
    /// </summary>
    private async Task ExpireOldestOpenRunsAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var open = await _dbContext.Runs
            .Where(r => r.UserId == userId && r.State == RunState.Open)
            .OrderBy(r => r.StartedAtUtc)
            .ToListAsync(cancellationToken);

        var excess = open.Count - (_options.MaxConcurrentOpenRuns - 1);

        if (excess <= 0)
            return;

        foreach (var stale in open.Take(excess))
        {
            stale.State = RunState.Expired;
            stale.EndedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private StartRunResponse Started(Run run) => new()
    {
        RunId = run.Id,
        GameId = run.GameId,
        Seed = run.Seed,
        StartedAtUtc = run.StartedAtUtc,
        ExpiresAtUtc = run.ExpiresAtUtc,
        ServerTimeUtc = DateTime.UtcNow
    };

    /// <summary>
    /// A non-negative 63-bit seed from the cryptographic RNG, not <c>Random</c>.
    /// <para>
    /// Not superstition: a predictable seed is a predictable layout, and the entire value of issuing
    /// one is that a client cannot know it before the server says so or choose a richer one by
    /// retrying. The sign bit is cleared so the value survives every JSON reader that treats numbers
    /// as signed, which is all of them.
    /// </para>
    /// </summary>
    private static long NextSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToInt64(bytes) & long.MaxValue;
    }

    // ------------------------------------------------------------- settling

    public async Task<ServiceResult<RunSettlementDto>> SettleAsync(
        Guid userId,
        Guid runId,
        SubmitRunResultRequest request,
        CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.Runs.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        // Step 1. A result for a run that was never started, or one belonging to somebody else, is
        // refused identically — answering "not yours" differently from "no such run" would turn this
        // route into an oracle for other people's run ids.
        if (run is null || run.UserId != userId)
            return ServiceResult<RunSettlementDto>.Failure(
                ApiErrors.RunNotFound,
                ServiceErrorKind.NotFound,
                $"Run {runId} was never started by this account.");

        // Already settled: replay the stored settlement. **Not a re-payment.** The client's offline
        // queue retries on reconnect by design, so a second result for one run is the ordinary path
        // and paying it again would mint currency on every dropped response.
        if (run.State == RunState.Settled)
            return ServiceResult<RunSettlementDto>.Success(
                await StoredSettlementAsync(run, userId, cancellationToken));

        var now = DateTime.UtcNow;

        if (run.State == RunState.Open && now > run.ExpiresAtUtc)
        {
            run.State = RunState.Expired;
            run.EndedAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (run.State == RunState.Expired)
            return ServiceResult<RunSettlementDto>.Failure(
                ApiErrors.RunExpired,
                ServiceErrorKind.Conflict,
                $"Run {runId} expired at {run.ExpiresAtUtc:O} and can no longer settle.");

        if (run.State == RunState.Rejected)
            return ServiceResult<RunSettlementDto>.Failure(
                ApiErrors.RunRejected,
                ServiceErrorKind.Conflict,
                $"Run {runId} was rejected as impossible against its own layout.");

        if (run.State != RunState.Open)
            return ServiceResult<RunSettlementDto>.Failure(
                ApiErrors.RunNotOpen,
                ServiceErrorKind.Conflict,
                $"Run {runId} is {WireEnum.ToWire(run.State)} and cannot settle.");

        var resultRequestId = Normalise(request.RequestId);

        if (resultRequestId is not null)
        {
            // One key, one run. A key already spent settling a *different* run is refused rather than
            // paid — that shape is either a client bug or an attempt to have one key pay twice, and
            // neither should move a balance.
            var spent = await _dbContext.Runs
                .AsNoTracking()
                .AnyAsync(
                    r => r.UserId == userId && r.ResultRequestId == resultRequestId && r.Id != runId,
                    cancellationToken);

            if (spent)
                return ServiceResult<RunSettlementDto>.Failure(
                    ApiErrors.RunRequestIdReused,
                    ServiceErrorKind.Conflict,
                    $"Request id '{resultRequestId}' has already settled a different run.");
        }

        var flags = new List<string>();
        var caps = new CapTracker();

        // Step 2. Bound the reported duration by real elapsed server time and take the smaller. An
        // unclamped duration is a free multiplier on every per-second bound, which is the whole reason
        // for having one — and the run was *started* here, so elapsed time is something the server
        // knows rather than something it is told.
        var elapsedMs = (now - run.StartedAtUtc).TotalMilliseconds;
        var durationMs = (int)Math.Clamp(Math.Min(request.DurationMs, elapsedMs), 0, int.MaxValue);

        if (request.DurationMs > elapsedMs)
            flags.Add("duration_clamped");

        if (durationMs < _options.MinRunDurationMs)
            // Flagged, never capped on its own. A genuine crash or an instant fail is short and
            // legitimate; whether the *claim* was possible in the time is MaxPickupsPerSecond's
            // question, not this one's.
            flags.Add("run_too_short");

        var collected = Aggregate(request.Pickups);

        WireEnum.TryFromWire<RunOutcome>(request.Outcome, out var outcome);

        var game = await _dbContext.Games
            .AsNoTracking()
            .FirstAsync(g => g.Id == run.GameId, cancellationToken);

        // Step 3. Layout verification, and the one place a run is refused rather than capped. Runs
        // first, because there is no point pricing a claim that is provably impossible.
        if (Verify(run, game.GameKey, collected, request.PickupIds) is { } rejection)
        {
            run.State = RunState.Rejected;
            run.EndedAtUtc = now;
            run.IsFlagged = true;
            run.FlagReason = rejection;
            run.PickupsJson = SerialisePickups(collected);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return ServiceResult<RunSettlementDto>.Failure(
                ApiErrors.RunRejected,
                ServiceErrorKind.Conflict,
                $"Run {runId} claims more than its own seeded layout contained ({rejection}).");
        }

        await CorroborateSessionAsync(run, userId, flags, cancellationToken);

        // Step 4. The account-wide farming bound. A run past it still settles and still records what
        // was collected — it simply pays nothing, and says so.
        var runsToday = await SettledRunsTodayAsync(userId, now, cancellationToken);
        var overRunLimit = runsToday >= _options.MaxRunsPerDay;

        if (overRunLimit)
        {
            caps.Reached("daily_run_limit");
            flags.Add("daily_run_limit");
        }

        var valuations = await ResolveValuationsAsync(run.GameId, collected.Keys, cancellationToken);
        var multiplier = ResolveMultiplier(request.Modifiers, durationMs, flags);

        var lines = overRunLimit
            ? []
            : await PriceAsync(userId, run, collected, valuations, durationMs, multiplier, now, caps, flags, cancellationToken);

        return await CommitSettlementAsync(
            run, userId, request, resultRequestId, durationMs, outcome,
            collected, lines, caps, flags, overRunLimit, cancellationToken);
    }

    // ------------------------------------------------------------- pricing

    /// <summary>
    /// Turns counts into amounts. Every clamp here is applied to a *count* before it becomes currency,
    /// which is what keeps the arithmetic auditable: the payout row records what was claimed, what was
    /// paid for, and the difference.
    /// <para>
    /// Order matters and it is narrowest-last: the per-run cap, then what is left of the kind's daily
    /// allowance, then what the run's own duration makes physically possible, then the modifier, then
    /// what is left of the account's daily ceiling for that currency.
    /// </para>
    /// </summary>
    private async Task<List<SettlementLine>> PriceAsync(
        Guid userId,
        Run run,
        IReadOnlyDictionary<string, int> collected,
        IReadOnlyDictionary<string, ResolvedValuation> valuations,
        int durationMs,
        long multiplier,
        DateTime now,
        CapTracker caps,
        List<string> flags,
        CancellationToken cancellationToken)
    {
        var priced = collected
            .Where(c => valuations.ContainsKey(c.Key))
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);

        if (priced.Count == 0)
            return [];

        var paidTodayByKind = await PaidTodayByKindAsync(userId, priced.Keys, now, cancellationToken);

        // A floor of one second, so a legitimate run shorter than that is not paid zero for arithmetic
        // reasons rather than for a reason anybody would defend.
        var seconds = Math.Max(1.0, durationMs / 1000.0);
        var perSecondCeiling = (int)Math.Min(int.MaxValue, Math.Ceiling(seconds * _options.MaxPickupsPerSecond));

        var lines = new List<SettlementLine>();

        foreach (var (kind, count) in priced)
        {
            var valuation = valuations[kind];
            var paidCount = count;

            if (paidCount > valuation.MaxPerRun)
            {
                paidCount = valuation.MaxPerRun;
                caps.Reached("pickup_limit");
                flags.Add("pickup_capped");
            }

            if (valuation.MaxPerDay is { } maxPerDay)
            {
                var remaining = Math.Max(0, maxPerDay - paidTodayByKind.GetValueOrDefault(kind));

                if (paidCount > remaining)
                {
                    paidCount = remaining;
                    caps.Reached("pickup_daily_limit");
                    flags.Add("pickup_daily_capped");
                }
            }

            if (paidCount > perSecondCeiling)
            {
                // The per-second bound. Load-bearing only because the duration it divides was already
                // clamped to real elapsed time — inflating the claimed duration to buy headroom here
                // does not work.
                paidCount = perSecondCeiling;
                caps.Reached("pickup_rate_limit");
                flags.Add("rate_capped");
            }

            var gross = count * valuation.UnitValue;
            var paidFace = paidCount * valuation.UnitValue;

            // Step 4 of the algorithm. **The server applies the multiplier.** The client declared that
            // a modifier ran; it never declared what its own payout should be. Applied to an
            // already-capped count, so it scales a bounded number rather than an open one.
            var net = paidFace * multiplier;

            if (net <= 0)
                continue;

            lines.Add(new SettlementLine(
                Source: $"pickup:{kind}",
                CurrencyId: valuation.CurrencyId,
                CollectedCount: count,
                PaidCount: paidCount,
                UnitValue: valuation.UnitValue,
                GrossAmount: gross,
                CappedAmount: gross - paidFace,
                NetAmount: net));
        }

        return await ApplyEarnCeilingAsync(userId, lines, caps, flags, cancellationToken);
    }

    /// <summary>
    /// Step 6 — the account's daily ceiling for each currency, applied last because it is the only
    /// bound denominated in currency rather than in pickups.
    /// <para>
    /// Lines are clamped in a stable order and share one pool, so a run collecting two kinds that pay
    /// the same currency cannot be paid the ceiling twice.
    /// </para>
    /// <para>
    /// **It bounds the pickup half only.** Reward rules grant a fixed, admin-authored amount bounded by
    /// their own <c>DailyLimit</c>; it is the variable payout, scaling with a claim the server did not
    /// choose, that needs a ceiling denominated in money. For a hard currency that is not enough on its
    /// own, which is why those bounds are enforced at authoring time instead — see
    /// <c>PickupValuationAdminService</c>.
    /// </para>
    /// </summary>
    private async Task<List<SettlementLine>> ApplyEarnCeilingAsync(
        Guid userId,
        List<SettlementLine> lines,
        CapTracker caps,
        List<string> flags,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
            return lines;

        var headroom = new Dictionary<Guid, long>(
            await _ceiling.HeadroomAsync(userId, lines.Select(l => l.CurrencyId).Distinct().ToList(), cancellationToken));

        var clamped = new List<SettlementLine>(lines.Count);

        foreach (var line in lines.OrderBy(l => l.Source, StringComparer.Ordinal))
        {
            var available = headroom.GetValueOrDefault(line.CurrencyId, long.MaxValue);

            if (available >= line.NetAmount)
            {
                if (available != long.MaxValue)
                    headroom[line.CurrencyId] = available - line.NetAmount;

                clamped.Add(line);
                continue;
            }

            caps.Reached("daily_coin_limit");

            if (!flags.Contains("daily_earn_capped"))
                flags.Add("daily_earn_capped");

            if (available <= 0)
                continue;

            headroom[line.CurrencyId] = 0;

            clamped.Add(line with
            {
                CappedAmount = line.CappedAmount + (line.NetAmount - available),
                NetAmount = available
            });
        }

        return clamped;
    }

    /// <summary>
    /// How many runs this account has settled today. Read from <c>Runs</c> rather than from
    /// <c>DailyCurrencyLedger.RunCount</c>, which counts per currency and misses a run that earned
    /// nothing — exactly the runs a farming script produces once the ceiling bites.
    /// </summary>
    private Task<int> SettledRunsTodayAsync(Guid userId, DateTime now, CancellationToken cancellationToken) =>
        _dbContext.Runs
            .AsNoTracking()
            .CountAsync(
                r => r.UserId == userId && r.State == RunState.Settled && r.EndedAtUtc >= now.Date,
                cancellationToken);

    /// <summary>
    /// How many of each kind this account was already **paid for** today. Paid, not claimed — a run
    /// capped down to 20 must not spend 47 of the day's allowance.
    /// </summary>
    private async Task<Dictionary<string, int>> PaidTodayByKindAsync(
        Guid userId,
        IEnumerable<string> kinds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var sources = kinds.Select(k => $"pickup:{k}").ToList();
        var day = now.Date;

        var totals = await (
                from payout in _dbContext.RunPayouts.AsNoTracking()
                join run in _dbContext.Runs.AsNoTracking() on payout.RunId equals run.Id
                where run.UserId == userId
                      && run.State == RunState.Settled
                      && run.EndedAtUtc >= day
                      && sources.Contains(payout.Source)
                group payout by payout.Source
                into grouped
                select new { Source = grouped.Key, Count = grouped.Sum(p => p.PaidCount) })
            .ToListAsync(cancellationToken);

        return totals.ToDictionary(
            t => t.Source["pickup:".Length..],
            t => t.Count,
            StringComparer.Ordinal);
    }

    // ------------------------------------------------------------- verification

    /// <summary>
    /// Re-derives the layout from the seed and checks the claim against it. Returns a machine token
    /// when the claim is **impossible**, null when it is merely improbable or unverifiable.
    /// <para>
    /// The only refusal in the whole feature, and it is earned: everything else caps because it is
    /// guessing, while this compares a claim against a layout the server itself generated. Collecting
    /// a coin that was never spawned, or the same coin twice, is not unlikely — it did not happen.
    /// </para>
    /// </summary>
    private string? Verify(Run run, string gameKey, IReadOnlyDictionary<string, int> collected, IReadOnlyList<int>? pickupIds)
    {
        if (run.LayoutVersion == 0)
            return null;

        // A retired generator version leaves a run unverified rather than unpayable. Removing a
        // version must not retroactively destroy runs queued offline against it.
        if (_layouts.Derive(gameKey, run.LayoutVersion, run.Seed) is not { } layout)
            return null;

        foreach (var (kind, count) in collected)
        {
            if (count > layout.PickupCounts.GetValueOrDefault(kind))
                return $"layout_exceeded:{kind}";
        }

        if (pickupIds is null || pickupIds.Count == 0)
            return null;

        if (pickupIds.Distinct().Count() != pickupIds.Count)
            return "layout_duplicate_pickup";

        return pickupIds.Any(id => !layout.PickupIds.Contains(id))
            ? "layout_unknown_pickup"
            : null;
    }

    /// <summary>
    /// A networked run declares the session it belonged to. Flagged, not refused, when the account was
    /// never in it — a session swept while a result sat in the offline queue looks identical to a
    /// forged one, and the child who actually played must not lose their run to the difference.
    /// </summary>
    private async Task CorroborateSessionAsync(
        Run run,
        Guid userId,
        List<string> flags,
        CancellationToken cancellationToken)
    {
        if (run.SessionId is not { } sessionId)
            return;

        var sessionExists = await _dbContext.MultiplayerSessions
            .AsNoTracking()
            .AnyAsync(s => s.Id == sessionId, cancellationToken);

        if (!sessionExists)
        {
            flags.Add("session_gone");
            return;
        }

        // Any membership, including one already left: the run happened while they were seated, and the
        // roster has moved on since.
        var wasMember = await _dbContext.MultiplayerSessionPlayers
            .AsNoTracking()
            .AnyAsync(p => p.SessionId == sessionId && p.UserId == userId, cancellationToken);

        if (!wasMember)
            flags.Add("session_unverified");
    }

    // ------------------------------------------------------------- committing

    /// <summary>
    /// Step 7 — <b>one transaction, or the feature is broken</b>. A grant without its payout rows is
    /// currency nobody can explain; a daily-ledger increment without its grant robs the child; and a
    /// run left <c>Open</c> after paying settles again on the client's next retry.
    /// </summary>
    private async Task<ServiceResult<RunSettlementDto>> CommitSettlementAsync(
        Run run,
        Guid userId,
        SubmitRunResultRequest request,
        string? resultRequestId,
        int durationMs,
        RunOutcome outcome,
        IReadOnlyDictionary<string, int> collected,
        List<SettlementLine> lines,
        CapTracker caps,
        List<string> flags,
        bool skipRules,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rewards = new List<RunRewardDto>();
        var earnedByCurrency = new Dictionary<Guid, long>();

        // --- the variable half: what the pickups were worth -------------------------------------
        foreach (var line in lines)
        {
            var applied = await _wallet.ApplyAsync(
                new WalletMutation
                {
                    UserId = userId,
                    CurrencyId = line.CurrencyId,
                    Delta = line.NetAmount,
                    TransactionType = CurrencyTransactionType.GameReward,
                    SourceType = LedgerSourceType.RunSettlement,
                    SourceId = run.Id.ToString(),
                    IdempotencyKey = $"run:{run.Id}:{line.Source}",
                    Metadata = JsonSerializer.Serialize(new
                    {
                        runId = run.Id,
                        gameId = run.GameId,
                        source = line.Source,
                        collected = line.CollectedCount,
                        paid = line.PaidCount,
                        unitValue = line.UnitValue
                    })
                },
                cancellationToken);

            // A retired currency, or any other refusal, loses the line rather than the run. The
            // pickups it could not pay for are still on the run as PickupsJson, so a corrected
            // valuation can be reconciled later; failing the whole settlement would lose them.
            if (!applied.Succeeded)
                continue;

            _dbContext.RunPayouts.Add(new RunPayout
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                CurrencyId = line.CurrencyId,
                Source = line.Source,
                CollectedCount = line.CollectedCount,
                PaidCount = line.PaidCount,
                UnitValue = line.UnitValue,
                GrossAmount = line.GrossAmount,
                CappedAmount = line.CappedAmount,
                NetAmount = line.NetAmount,
                CreatedAtUtc = now
            });

            rewards.Add(new RunRewardDto
            {
                Currency = applied.Value!.Currency,
                Amount = line.NetAmount,
                Source = line.Source
            });

            Accumulate(earnedByCurrency, line.CurrencyId, line.NetAmount);
        }

        // Saved **before** the reward engine runs, deliberately. It creates savepoints and rolls back
        // to them when a rule cannot pay; anything of ours still sitting unsaved in the change tracker
        // would be swept into that savepoint and silently rolled back with it.
        await _dbContext.SaveChangesAsync(cancellationToken);

        // --- the fixed half: reward rules on RUN_SETTLED -----------------------------------------
        // Skipped entirely past the daily run limit. A run that pays nothing for its pickups must not
        // still hand out a completion bonus, or the wall a farming script hits has a door in it.
        if (!skipRules)
            await PayRuleRewardsAsync(run, userId, durationMs, outcome, now, rewards, earnedByCurrency, cancellationToken);

        // --- the counter the ceiling reads --------------------------------------------------------
        foreach (var (currencyId, amount) in earnedByCurrency)
            await _ceiling.AccrueAsync(userId, currencyId, amount, now, cancellationToken);

        // --- close the run -----------------------------------------------------------------------
        run.State = RunState.Settled;
        run.EndedAtUtc = now;
        run.Outcome = outcome;
        run.DurationMs = durationMs;
        run.ResultRequestId = resultRequestId;
        run.CapReached = caps.Any;
        run.CapMessage = caps.Message;
        run.IsFlagged = flags.Count > 0;
        run.FlagReason = flags.Count > 0 ? string.Join(",", flags.Distinct()) : null;
        run.PickupsJson = SerialisePickups(collected);
        run.ModifiersJson = request.Modifiers.Count > 0
            ? JsonSerializer.Serialize(request.Modifiers.Select(m => new
            {
                kind = m.Kind,
                durationSeconds = m.DurationSeconds
            }))
            : null;

        // Ranking's seam into runs, and the only one. Inside the transaction for the same reason the
        // attempt path puts it there: these results are the source of truth for every board and every
        // objective, so a run that committed without them would be a rank and a quest step silently
        // lost. It writes rows and queues a job — no board is walked here, so finishing a run never
        // waits on a leaderboard.
        await _gameResults.RecordAsync(
            new GameResultContext
            {
                UserId = userId,
                GameId = run.GameId,
                SourceId = run.Id,
                OccurredAtUtc = now,
                GradeId = await ResolveGradeIdAsync(userId, cancellationToken),
                LangId = await _languageService.ResolveCurrentAsync(cancellationToken),
                RequestId = resultRequestId,
                SourceType = GameResultSource.Session,
                // A run held back for review must not rank or advance an objective while it sits
                // there. The per-metric bounds cannot see that; the run already decided.
                PreFlagged = run.IsFlagged,
                PreFlagReason = run.FlagReason,
                Metrics = RunMetricsFor(run, durationMs, outcome, collected, lines, earnedByCurrency)
            },
            cancellationToken);

        // Same inline fold as the attempt path, for the same reason — a "play three runs" daily
        // must tick over the moment the third run ends, not on the next batch pass.
        await _objectives.ProjectForUserAsync(userId, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Read after the commit so what goes back is committed truth, and so the transaction is not
        // held open across it.
        var balances = await _wallet.GetBalancesAsync(userId, cancellationToken);

        return ServiceResult<RunSettlementDto>.Success(new RunSettlementDto
        {
            RunId = run.Id,
            GameId = run.GameId,
            State = WireEnum.ToWire(run.State),
            Outcome = WireEnum.ToWire(run.Outcome),
            Collected = collected.Select(c => new RunCollectedDto { Kind = c.Key, Count = c.Value }).ToList(),
            Rewards = Ordered(rewards),
            Balances = balances,
            CapReached = run.CapReached,
            CapMessage = run.CapMessage,
            ServerTimeUtc = DateTime.UtcNow
        });
    }

    private async Task PayRuleRewardsAsync(
        Run run,
        Guid userId,
        int durationMs,
        RunOutcome outcome,
        DateTime now,
        List<RunRewardDto> rewards,
        Dictionary<Guid, long> earnedByCurrency,
        CancellationToken cancellationToken)
    {
        var ruleRewards = await _rewards.EvaluateRunSettlementAsync(
            new RunRewardContext
            {
                UserId = userId,
                GameId = run.GameId,
                RunId = run.Id,
                DurationMs = durationMs,
                Outcome = outcome
            },
            cancellationToken);

        if (ruleRewards.Count == 0)
            return;

        var keys = ruleRewards.SelectMany(r => r.Grants).Select(g => g.Currency).Distinct().ToList();

        var currencyIds = await _dbContext.Currencies
            .AsNoTracking()
            .Where(c => keys.Contains(c.Key))
            .ToDictionaryAsync(c => c.Key, c => c.Id, cancellationToken);

        foreach (var reward in ruleRewards)
        {
            foreach (var grant in reward.Grants)
            {
                if (!currencyIds.TryGetValue(grant.Currency, out var currencyId))
                    continue;

                var source = $"rule:{reward.RuleId}";

                // Counts and unit value are zero: a rule bonus is a fixed amount that scales with
                // nothing, which is exactly why it could not be expressed as a valuation row.
                _dbContext.RunPayouts.Add(new RunPayout
                {
                    Id = Guid.NewGuid(),
                    RunId = run.Id,
                    CurrencyId = currencyId,
                    Source = source,
                    CollectedCount = 0,
                    PaidCount = 0,
                    UnitValue = 0,
                    GrossAmount = grant.Amount,
                    CappedAmount = 0,
                    NetAmount = grant.Amount,
                    CreatedAtUtc = now
                });

                rewards.Add(new RunRewardDto
                {
                    Currency = grant.Currency,
                    Amount = grant.Amount,
                    Source = source
                });

                Accumulate(earnedByCurrency, currencyId, grant.Amount);
            }
        }
    }

    /// <summary>
    /// Rebuilds a settled run's answer from its own payout rows rather than from a stored response
    /// body.
    /// <para>
    /// Cheaper and more honest than storing the JSON: the balances are re-read live, so a replay
    /// arriving after the child has spent some of it reports what they actually hold rather than a
    /// stale number the client would then assign over the truth.
    /// </para>
    /// </summary>
    private async Task<RunSettlementDto> StoredSettlementAsync(
        Run run,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var payouts = await _dbContext.RunPayouts
            .AsNoTracking()
            .Include(p => p.Currency)
            .Where(p => p.RunId == run.Id)
            .ToListAsync(cancellationToken);

        var balances = await _wallet.GetBalancesAsync(userId, cancellationToken);
        var collected = JsonSerializer.Deserialize<List<StoredPickup>>(run.PickupsJson, RunJson.Options) ?? [];

        return new RunSettlementDto
        {
            RunId = run.Id,
            GameId = run.GameId,
            State = WireEnum.ToWire(run.State),
            Outcome = WireEnum.ToWire(run.Outcome),
            Collected = collected
                .Select(c => new RunCollectedDto { Kind = c.Kind, Count = c.Count })
                .ToList(),
            Rewards = Ordered(payouts.Select(p => new RunRewardDto
            {
                Currency = p.Currency!.Key,
                Amount = p.NetAmount,
                Source = p.Source
            })),
            Balances = balances,
            CapReached = run.CapReached,
            CapMessage = run.CapMessage,
            ServerTimeUtc = DateTime.UtcNow
        };
    }

    // ------------------------------------------------------------- valuation

    private sealed record ResolvedValuation(Guid CurrencyId, long UnitValue, int MaxPerRun, int? MaxPerDay);

    private sealed record SettlementLine(
        string Source,
        Guid CurrencyId,
        int CollectedCount,
        int PaidCount,
        long UnitValue,
        long GrossAmount,
        long CappedAmount,
        long NetAmount);

    private sealed record StoredPickup(string Kind, int Count);

    /// <summary>
    /// Which cap actually cost the player the most, so the results screen explains the shortfall it
    /// can least afford to leave unexplained.
    /// <para>
    /// Ordered most-restrictive first: paying nothing because the day's runs are exhausted needs a
    /// different sentence from being clipped at a per-run limit, and showing the smaller reason when
    /// the larger one applied is how "why did I get nothing?" becomes a support ticket.
    /// </para>
    /// </summary>
    private sealed class CapTracker
    {
        private static readonly string[] Precedence =
            ["daily_run_limit", "daily_coin_limit", "pickup_daily_limit", "pickup_rate_limit", "pickup_limit"];

        private int _best = int.MaxValue;

        public bool Any => _best != int.MaxValue;

        public string? Message => Any ? Precedence[_best] : null;

        public void Reached(string token)
        {
            var rank = Array.IndexOf(Precedence, token);

            if (rank >= 0 && rank < _best)
                _best = rank;
        }
    }

    /// <summary>
    /// Resolution order: an exact <c>(gameId, kind)</c> row, then the <c>GameId IS NULL</c> platform
    /// default, then nothing — which means the kind pays zero.
    /// <para>
    /// Rows for a retired currency are excluded here rather than failing later, so a currency being
    /// retired quietly stops paying instead of producing a settlement full of refused lines.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, ResolvedValuation>> ResolveValuationsAsync(
        Guid gameId,
        IEnumerable<string> kinds,
        CancellationToken cancellationToken)
    {
        var wanted = kinds.ToList();

        if (wanted.Count == 0)
            return [];

        var rows = await _dbContext.PickupValuations
            .AsNoTracking()
            .Include(v => v.Currency)
            .Where(v => v.Enabled
                        && wanted.Contains(v.PickupKind)
                        && (v.GameId == gameId || v.GameId == null))
            .ToListAsync(cancellationToken);

        var resolved = new Dictionary<string, ResolvedValuation>(StringComparer.Ordinal);

        // Game-specific last so it overwrites the default it shares a kind with. Ordering here rather
        // than filtering in SQL keeps it to one round trip and one obvious precedence rule.
        foreach (var row in rows.OrderBy(v => v.GameId.HasValue))
        {
            if (row.Currency is null || !row.Currency.Enabled)
                continue;

            resolved[row.PickupKind] = new ResolvedValuation(
                row.CurrencyId, row.UnitValue, row.MaxPerRun, row.MaxPerDay);
        }

        return resolved;
    }

    /// <summary>
    /// Sums duplicate entries for one kind and drops anything that is not a legal token. A kind
    /// reported twice is one total that meets one cap — splitting it across entries must not buy a
    /// second helping of <see cref="PickupValuation.MaxPerRun"/>.
    /// </summary>
    private static Dictionary<string, int> Aggregate(IEnumerable<RunPickupReport> pickups)
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var pickup in pickups)
        {
            var kind = PickupKinds.Normalise(pickup.Kind);

            if (kind is null || pickup.Count <= 0)
                continue;

            totals[kind] = totals.TryGetValue(kind, out var running)
                ? running + pickup.Count
                : pickup.Count;
        }

        return totals;
    }

    /// <summary>
    /// What the declared modifiers multiply the pickup payout by.
    /// <para>
    /// <b>A declared modifier is applied without corroborating that it was obtained</b>, and that is a
    /// stated gap rather than a hidden one: proving it needs the seeded layout to place modifier
    /// pickups too, or the host-authoritative <c>PickupLedger</c> for a networked run. Until then the
    /// defences are that a modifier claiming to have outlasted its own run is clamped and flagged, and
    /// that the multiplier scales an already-capped count rather than an open one.
    /// </para>
    /// </summary>
    private static long ResolveMultiplier(
        IEnumerable<RunModifierReport> modifiers,
        int durationMs,
        List<string> flags)
    {
        var multiplier = 1L;

        foreach (var modifier in modifiers)
        {
            // An unrecognised kind is ignored rather than refused: an older server must not fail a run
            // produced by a newer client, and ignoring it can only ever pay less.
            if (!WireEnum.TryFromWire<RunModifierKind>(modifier.Kind, out var kind)
                || kind == RunModifierKind.Unknown)
                continue;

            if (modifier.DurationSeconds <= 0)
                continue;

            if (modifier.DurationSeconds * 1000 > durationMs)
                // A modifier cannot have been active longer than the run that contained it. Clamped
                // and flagged rather than refused — a resumed session legitimately produces this.
                flags.Add("modifier_clamped");

            if (kind == RunModifierKind.DoubleReward)
                multiplier *= 2;
        }

        return multiplier;
    }

    // ------------------------------------------------------------- plumbing

    private static string SerialisePickups(IReadOnlyDictionary<string, int> collected) =>
        JsonSerializer.Serialize(collected.Select(c => new { kind = c.Key, count = c.Value }), RunJson.Options);

    private static void Accumulate(Dictionary<Guid, long> totals, Guid currencyId, long amount) =>
        totals[currencyId] = totals.TryGetValue(currencyId, out var running) ? running + amount : amount;

    /// <summary>
    /// Ordered by currency then source so two settlements of the same run produce byte-identical
    /// output, and so a replay cannot look like a different answer to the same question.
    /// </summary>
    private static IReadOnlyList<RunRewardDto> Ordered(IEnumerable<RunRewardDto> rewards) =>
        rewards
            .OrderBy(r => r.Currency, StringComparer.Ordinal)
            .ThenBy(r => r.Source, StringComparer.Ordinal)
            .ToList();

    private static string? Normalise(string? requestId)
    {
        var trimmed = (requestId ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private void Detach(Run run)
    {
        var entry = _dbContext.Entry(run);

        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    /// <summary>
    /// A row pointing at something that is not there — in practice the user, whose token can outlive
    /// the account. Disjoint from <see cref="IsUniqueViolation"/>, which is a retryable collision.
    /// </summary>
    private static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 547 };

    /// <summary>
    /// What a settled run is worth to a leaderboard or an objective.
    /// <para>
    /// **Every count here is what was <i>settled</i>, never what was reported.** A claim of 500
    /// coins that the per-run cap settled at 180 raises 180 — otherwise a "collect 500" objective
    /// pays out on a number the economy already refused, which is the cap defeated through a side
    /// door. Duration is the server-bounded figure for the same reason.
    /// </para>
    /// <para>
    /// A run that settled nothing still raises <c>RUNS_SETTLED</c>: playing is the thing being
    /// measured, and a daily "play three runs" must not depend on how well they went.
    /// </para>
    /// </summary>
    private static IReadOnlyList<GameResultDraft> RunMetricsFor(
        Run run,
        int durationMs,
        RunOutcome outcome,
        IReadOnlyDictionary<string, int> collected,
        List<SettlementLine> lines,
        Dictionary<Guid, long> earnedByCurrency)
    {
        var metrics = new List<GameResultDraft>(6 + lines.Count);

        metrics.Add(new GameResultDraft(LeaderboardMetrics.RunsSettled, 1));

        if (outcome == RunOutcome.Completed)
            metrics.Add(new GameResultDraft(LeaderboardMetrics.RunsCompleted, 1));

        // Whole seconds. A sub-second run is a real run but rounds to nothing, and the recorder
        // drops zeroes — which is correct: it contributes nothing to a Sum and cannot win a Best.
        var seconds = durationMs / 1000;

        if (seconds > 0)
        {
            metrics.Add(new GameResultDraft(LeaderboardMetrics.RunSeconds, seconds));
            metrics.Add(new GameResultDraft(LeaderboardMetrics.BestRunSeconds, seconds));
        }

        // Paid counts, scoped by kind — one metric for every pickup kind any mini-game ever adds.
        foreach (var line in lines)
        {
            if (line.PaidCount > 0)
                metrics.Add(new GameResultDraft(
                    LeaderboardMetrics.PickupsCollected, line.PaidCount, line.Source));
        }

        return metrics;
    }

    /// <summary>
    /// The player's grade as it is right now, snapshotted onto the result. Null when they have no
    /// profile yet — a run is playable before a grade is chosen, and an ungraded result still
    /// belongs on the all-grades ladders.
    /// </summary>
    private async Task<Guid?> ResolveGradeIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await _dbContext.StudentProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.GradeId)
            .FirstOrDefaultAsync(cancellationToken);

}
