using Share7.Infrastructure.Objectives;
using Share7.Infrastructure.Leaderboards;
using Share7.Domain.Constants;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Application.Runs.Interfaces;
using Share7.Application.Runs.Models;
using Share7.Domain.Economy;
using Share7.Domain.Games;
using Share7.Domain.Runs;
using Share7.Infrastructure.Economy;
using Share7.Infrastructure.Persistence;
using Share7.Infrastructure.Progression;
using Share7.Infrastructure.Rewards;
using Share7.Infrastructure.Runs;

namespace Share7.Tests.Infrastructure;

public static class RunTestExtensions
{
    /// <summary>
    /// The real service graph, wired by hand. No mocks: the point of these tests is that settlement,
    /// rewards, the wallet and the daily counter compose over **one** DbContext and therefore one
    /// transaction — substituting any of them would hide exactly that.
    /// <para>
    /// The layout verifier defaults to none registered, which is the shipped state: verification off,
    /// plausibility bounds only. Pass generators to turn it on for a game.
    /// </para>
    /// </summary>
    public static RunService CreateRunService(
        ApplicationDbContext context,
        IWalletService? wallet = null,
        RunOptions? options = null,
        Guid? langId = null,
        params IRunLayoutGenerator[] generators)
    {
        var resolved = wallet ?? new WalletService(context);

        return new RunService(
            context,
            resolved,
            new RewardService(context, resolved, new LevelService(context), new Share7.Infrastructure.Commerce.EntitlementService(context)),
            new EarnCeilingService(context),
            // The real pricer, for the same reason the recorder below is real: it reads and writes the
            // counters the settlement's caps depend on, inside the same transaction.
            new SignalPricer(context, new EarnCeilingService(context), Options.Create(options ?? new RunOptions())),
            new LevelService(context),
            new RunLayoutVerifier(generators),
            // The real recorder, not a stub: it writes into the same DbContext and therefore the
            // same transaction as the settlement, which is the property worth testing — a result
            // that survived a rolled-back settlement would be a rank for a run that never paid.
            new GameResultRecorder(
                context,
                new PlausibilityGuard(context),
                NullLogger<GameResultRecorder>.Instance),
            new ObjectiveProjector(context, NullLogger<ObjectiveProjector>.Instance),
            new StubLanguageService(langId ?? LanguageIds.English),
            Options.Create(options ?? Permissive()));
    }

    public static RunAdminService CreateRunAdminService(ApplicationDbContext context) => new(context);

    /// <summary>
    /// Default options for a test, with the per-second bound effectively lifted.
    /// <para>
    /// **Because the clock does not advance in a test the way it does in a run.** A settlement here
    /// happens milliseconds after its start, so the duration clamp — correctly — reduces a claimed
    /// 90-second run to nearly zero elapsed time, and every claim then looks physically impossible. In
    /// production the run really did take ninety seconds and the bound never fires on it.
    /// </para>
    /// <para>
    /// <c>MinRunDurationMs</c> goes with it for the same reason: an instant settlement is an instant
    /// run, and every test would otherwise come back flagged <c>run_too_short</c>.
    /// </para>
    /// <para>
    /// <c>RunBoundsTests</c> sets the real values and ages its runs, so the bounds themselves are under
    /// test where they are the subject rather than incidental scenery.
    /// </para>
    /// </summary>
    public static RunOptions Permissive() => new() { MaxPickupsPerSecond = 1_000_000, MinRunDurationMs = 0 };

    /// <summary>
    /// Pushes a run's start time backwards so the elapsed clock looks like real play.
    /// <para>
    /// The alternative is a test that sleeps for a minute. Written straight to the database because
    /// the point is to move time underneath the service, not to ask it to pretend.
    /// </para>
    /// </summary>
    public static async Task AgeRunAsync(this ApplicationDbContext context, Guid runId, int seconds)
    {
        await context.Runs
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(
                r => r.StartedAtUtc, r => r.StartedAtUtc.AddSeconds(-seconds)));

        // ExecuteUpdate goes straight to the database and past the change tracker, so a context that
        // started this run is still holding the old StartedAtUtc and would hand it back to the very
        // service this call is trying to fool. Production never sees it — one scoped DbContext per
        // request — but a test that starts and settles on one context does.
        var tracked = context.ChangeTracker.Entries<Run>().FirstOrDefault(e => e.Entity.Id == runId);

        if (tracked is not null)
            await tracked.ReloadAsync();
    }

    public static async Task<Game> CreateGameAsync(
        this ApplicationDbContext context,
        bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var game = new Game
        {
            Id = Guid.NewGuid(),
            GameKey = $"g_{Guid.NewGuid():N}"[..20],
            IsActive = isActive
        };

        context.Games.Add(game);
        await context.SaveChangesAsync(cancellationToken);
        return game;
    }

    /// <summary>A price for one signal kind. <paramref name="gameId"/> null makes it the platform default.</summary>
    public static async Task<SignalValuation> CreateValuationAsync(
        this ApplicationDbContext context,
        Guid currencyId,
        string kind = SignalKinds.Coin,
        Guid? gameId = null,
        long unitValue = 1,
        int maxPerRun = 500,
        int? maxPerDay = null,
        double? maxPerSecond = null,
        bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var valuation = new SignalValuation
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            SignalKind = kind,
            CurrencyId = currencyId,
            UnitValue = unitValue,
            MaxPerRun = maxPerRun,
            MaxPerDay = maxPerDay,
            MaxPerSecond = maxPerSecond,
            Enabled = enabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.SignalValuations.Add(valuation);
        await context.SaveChangesAsync(cancellationToken);
        return valuation;
    }

    /// <summary>A currency with a daily earning ceiling, or a hard one.</summary>
    public static async Task<Currency> CreateCappedCurrencyAsync(
        this ApplicationDbContext context,
        long? dailyEarnCap = null,
        bool isHard = false,
        CancellationToken cancellationToken = default)
    {
        var currency = new Currency
        {
            Id = Guid.NewGuid(),
            Key = $"c{Guid.NewGuid():N}"[..16],
            Name = "Test Currency",
            Enabled = true,
            IsHard = isHard,
            DailyEarnCap = dailyEarnCap,
            CreatedAtUtc = DateTime.UtcNow
        };

        context.Currencies.Add(currency);
        await context.SaveChangesAsync(cancellationToken);
        return currency;
    }

    public static SubmitRunResultRequest Result(
        int coins = 0,
        string kind = SignalKinds.Coin,
        int durationMs = 60_000,
        string outcome = nameof(RunOutcome.Completed),
        string? requestId = null,
        params RunModifierReport[] modifiers) => new()
    {
        Pickups = coins > 0 ? [new RunSignalReport { Kind = kind, Count = coins }] : [],
        Modifiers = [.. modifiers],
        DurationMs = durationMs,
        Outcome = outcome,
        RequestId = requestId
    };

    public static RunModifierReport DoubleReward(double seconds = 10) =>
        new() { Kind = "double_reward", DurationSeconds = seconds };

    public static Task<List<RunPayout>> PayoutsOfAsync(
        this ApplicationDbContext context,
        Guid runId,
        CancellationToken cancellationToken = default) =>
        context.RunPayouts
            .AsNoTracking()
            .Where(p => p.RunId == runId)
            .OrderBy(p => p.Source)
            .ToListAsync(cancellationToken);

    public static Task<DailyCurrencyLedger?> DailyOfAsync(
        this ApplicationDbContext context,
        Guid userId,
        Guid currencyId,
        CancellationToken cancellationToken = default) =>
        context.DailyCurrencyLedger
            .AsNoTracking()
            .FirstOrDefaultAsync(
                l => l.UserId == userId && l.CurrencyId == currencyId && l.DayUtc == DateTime.UtcNow.Date,
                cancellationToken);

    public static Task<Run> RunOfAsync(
        this ApplicationDbContext context,
        Guid runId,
        CancellationToken cancellationToken = default) =>
        context.Runs.AsNoTracking().SingleAsync(r => r.Id == runId, cancellationToken);

    /// <summary>Absolute amount for one currency out of a settlement's <c>balances</c>.</summary>
    public static long AmountOf(this IReadOnlyList<BalanceDto> balances, string currencyKey) =>
        balances.FirstOrDefault(b => b.Currency == currencyKey)?.Amount ?? 0;

    /// <summary>
    /// A deterministic stand-in for a real ported track generator: <paramref name="counts"/> of each
    /// kind, with pickup ids <c>0..n-1</c>.
    /// <para>
    /// **Not a model of the real algorithm and not pretending to be.** It exists so the verification
    /// *path* — reject more than the layout held, reject an id that was never placed, reject the same
    /// id twice — is under test without waiting on the Unity port. A real generator has to reproduce
    /// the client's output bit for bit; this one only has to be deterministic.
    /// </para>
    /// </summary>
    public sealed class FixedLayoutGenerator : IRunLayoutGenerator
    {
        private readonly RunLayout _layout;

        public FixedLayoutGenerator(string gameKey, int version, params (string Kind, int Count)[] counts)
        {
            GameKey = gameKey;
            Version = version;

            var byKind = counts.ToDictionary(c => c.Kind, c => c.Count, StringComparer.Ordinal);
            var total = counts.Sum(c => c.Count);

            _layout = new RunLayout(byKind, Enumerable.Range(0, total).ToHashSet());
        }

        public string GameKey { get; }
        public int Version { get; }

        public RunLayout Generate(long seed) => _layout;
    }

    /// <summary>
    /// The real wallet, rigged to throw on the nth successful grant.
    /// <para>
    /// Used to prove step 7 is genuinely one transaction. A refusal is not enough for that — a refused
    /// line is deliberately skipped rather than fatal — so this raises something settlement does not
    /// catch, which is the only way to observe whether the writes already made survive.
    /// </para>
    /// </summary>
    public sealed class ThrowingWallet : IWalletService
    {
        private readonly IWalletService _inner;
        private readonly int _throwOnCall;
        private int _calls;

        public ThrowingWallet(ApplicationDbContext context, int throwOnCall)
        {
            _inner = new WalletService(context);
            _throwOnCall = throwOnCall;
        }

        public Task<IReadOnlyList<BalanceDto>> GetBalancesAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            _inner.GetBalancesAsync(userId, cancellationToken);

        public Task<ServiceResult<WalletMutationResult>> ApplyAsync(
            WalletMutation mutation, CancellationToken cancellationToken = default)
        {
            if (++_calls == _throwOnCall)
                throw new InvalidOperationException("wallet exploded mid-settlement");

            return _inner.ApplyAsync(mutation, cancellationToken);
        }
    }
}
