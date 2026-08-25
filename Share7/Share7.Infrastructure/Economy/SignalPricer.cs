using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Economy.Interfaces;
using Share7.Application.Economy.Models;
using Share7.Application.Runs.Models;
using Share7.Domain.Economy;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Economy;

/// <inheritdoc cref="ISignalPricer"/>
public class SignalPricer : ISignalPricer
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEarnCeilingService _ceiling;
    private readonly RunOptions _options;

    public SignalPricer(
        ApplicationDbContext dbContext,
        IEarnCeilingService ceiling,
        IOptions<RunOptions> options)
    {
        _dbContext = dbContext;
        _ceiling = ceiling;
        _options = options.Value;
    }

    public async Task<SignalPricing> PriceAsync(
        SignalPricingRequest request,
        CancellationToken cancellationToken = default)
    {
        // Step 1. Drop what this surface does not own. A run reporting `correct_answer` is either an
        // old client or a modified one; either way the attempt path is what pays for answers, and
        // paying here as well would pay for one right answer twice, from two transactions that
        // cannot see each other.
        var owned = request.Counts
            .Where(c => c.Value > 0 && SignalKinds.IsReportableBy(c.Key, request.Surface))
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);

        if (owned.Count == 0)
            return SignalPricing.Empty;

        var valuations = await ResolveAsync(request.GameId, owned.Keys, cancellationToken);

        // An unpriced kind pays zero and does not fail the session. That is a design oversight to
        // notice in the payout data, not a reason to lose a child's run.
        var priced = owned
            .Where(c => valuations.ContainsKey(c.Key))
            .ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);

        if (priced.Count == 0)
            return SignalPricing.Empty;

        var paidToday = await PaidTodayAsync(request.UserId, priced.Keys, request.NowUtc, cancellationToken);

        var caps = new CapLadder();
        var flags = new List<string>();
        var lines = new List<SignalLine>();

        foreach (var (kind, reported) in priced.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var valuation = valuations[kind];
            var paidCount = reported;

            // Step 2. The per-session cap. Mandatory on every row, so this always bites somewhere.
            if (paidCount > valuation.MaxPerRun)
            {
                paidCount = valuation.MaxPerRun;
                caps.Reached("signal_limit");
                flags.Add("signal_capped");
            }

            // Step 3. What is left of the kind's daily allowance, across every session and both
            // surfaces — one keyed read rather than a scan over payout history.
            if (valuation.MaxPerDay is { } maxPerDay)
            {
                var remaining = Math.Max(0, maxPerDay - paidToday.GetValueOrDefault(kind));

                if (paidCount > remaining)
                {
                    paidCount = remaining;
                    caps.Reached("signal_daily_limit");
                    flags.Add("signal_daily_capped");
                }
            }

            // Step 4. What the session's own duration makes physically possible. Per kind, because
            // one global rate cannot be right for both a coin and a metre — see
            // SignalValuation.MaxPerSecond. Skipped entirely on a surface with no duration.
            if (PerSecondCeiling(request, valuation) is { } ceiling && paidCount > ceiling)
            {
                paidCount = ceiling;
                caps.Reached("signal_rate_limit");
                flags.Add("rate_capped");
            }

            var gross = (long)reported * valuation.UnitValue;
            var paidFace = (long)paidCount * valuation.UnitValue;

            // Step 5. **The server applies the multiplier.** The client declared that a modifier ran;
            // it never declared what its own payout should be. Applied to an already-capped count, so
            // it scales a bounded number rather than an open one.
            var net = paidFace * Math.Max(1, request.Multiplier);

            if (net <= 0)
                continue;

            lines.Add(new SignalLine
            {
                Kind = kind,
                Source = SourceFor(kind),
                CurrencyId = valuation.CurrencyId,
                CurrencyKey = valuation.CurrencyKey,
                ReportedCount = reported,
                PaidCount = paidCount,
                UnitValue = valuation.UnitValue,
                GrossAmount = gross,
                CappedAmount = gross - paidFace,
                NetAmount = net
            });
        }

        if (lines.Count == 0)
            return new SignalPricing { Lines = [], CapMessages = caps.Messages, Flags = flags };

        // Step 6. The account's daily ceiling for each currency, applied last because it is the only
        // bound denominated in currency rather than in signals.
        var clamped = request.BypassEarnCeiling
            ? lines
            : await ApplyEarnCeilingAsync(request.UserId, lines, caps, flags, cancellationToken);

        return new SignalPricing
        {
            Lines = clamped,
            CapMessages = caps.Messages,
            Flags = flags
        };
    }

    public async Task AccrueAsync(
        Guid userId,
        IReadOnlyList<SignalLine> granted,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (granted.Count == 0)
            return;

        foreach (var line in granted.Where(l => l.PaidCount > 0))
            await AccrueSignalAsync(userId, line.Kind, line.PaidCount, nowUtc, cancellationToken);

        foreach (var group in granted.GroupBy(l => l.CurrencyId))
        {
            var amount = group.Sum(l => l.NetAmount);

            if (amount > 0)
                await _ceiling.AccrueAsync(userId, group.Key, amount, nowUtc, cancellationToken);
        }
    }

    /// <summary>
    /// Provenance stamped on every ledger entry and returned to the client. <c>signal:</c> rather
    /// than the historical <c>pickup:</c>, because the set now includes things nobody picks up.
    /// </summary>
    public static string SourceFor(string kind) => $"signal:{kind}";

    // ------------------------------------------------------------- resolution

    private sealed record ResolvedValuation(
        Guid CurrencyId,
        string CurrencyKey,
        long UnitValue,
        int MaxPerRun,
        int? MaxPerDay,
        double? MaxPerSecond);

    /// <summary>
    /// Every enabled price for this game plus the platform defaults, in one query rather than one per
    /// kind. A row whose currency has been retired is skipped, so retiring a currency quietly stops
    /// paying instead of producing a settlement full of refused lines.
    /// </summary>
    private async Task<Dictionary<string, ResolvedValuation>> ResolveAsync(
        Guid gameId,
        IEnumerable<string> kinds,
        CancellationToken cancellationToken)
    {
        var wanted = kinds.ToList();

        if (wanted.Count == 0)
            return [];

        var rows = await _dbContext.SignalValuations
            .AsNoTracking()
            .Include(v => v.Currency)
            .Where(v => v.Enabled
                        && wanted.Contains(v.SignalKind)
                        && (v.GameId == gameId || v.GameId == null))
            .ToListAsync(cancellationToken);

        var resolved = new Dictionary<string, ResolvedValuation>(StringComparer.Ordinal);

        // Game-specific last so it overwrites the default it shares a kind with. Ordering here rather
        // than filtering in SQL keeps it to one round trip and one obvious precedence rule.
        foreach (var row in rows.OrderBy(v => v.GameId.HasValue))
        {
            if (row.Currency is null || !row.Currency.Enabled)
                continue;

            resolved[row.SignalKind] = new ResolvedValuation(
                row.CurrencyId,
                row.Currency.Key,
                row.UnitValue,
                row.MaxPerRun,
                row.MaxPerDay,
                row.MaxPerSecond);
        }

        return resolved;
    }

    /// <summary>
    /// The per-second bound for one kind, or null when the surface has no duration to divide by.
    /// A floor of one second, so a legitimate session shorter than that is not paid zero for
    /// arithmetic reasons rather than for a reason anybody would defend.
    /// </summary>
    private int? PerSecondCeiling(SignalPricingRequest request, ResolvedValuation valuation)
    {
        if (request.DurationMs is not { } durationMs)
            return null;

        var perSecond = valuation.MaxPerSecond ?? _options.MaxPickupsPerSecond;

        if (perSecond <= 0)
            return null;

        var seconds = Math.Max(1.0, durationMs / 1000.0);

        return (int)Math.Min(int.MaxValue, Math.Ceiling(seconds * perSecond));
    }

    private async Task<Dictionary<string, int>> PaidTodayAsync(
        Guid userId,
        IEnumerable<string> kinds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var wanted = kinds.ToList();
        var day = nowUtc.Date;

        return await _dbContext.DailySignalLedger
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.DayUtc == day && wanted.Contains(l.SignalKind))
            .ToDictionaryAsync(l => l.SignalKind, l => l.PaidCount, StringComparer.Ordinal, cancellationToken);
    }

    /// <summary>
    /// The account's daily ceiling for each currency.
    /// <para>
    /// Lines are clamped in a stable order and share one pool, so a session earning two kinds that
    /// pay the same currency cannot be paid the ceiling twice.
    /// </para>
    /// </summary>
    private async Task<List<SignalLine>> ApplyEarnCeilingAsync(
        Guid userId,
        List<SignalLine> lines,
        CapLadder caps,
        List<string> flags,
        CancellationToken cancellationToken)
    {
        var headroom = new Dictionary<Guid, long>(
            await _ceiling.HeadroomAsync(
                userId, lines.Select(l => l.CurrencyId).Distinct().ToList(), cancellationToken));

        var clamped = new List<SignalLine>(lines.Count);

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
    /// UPDLOCK serialises two sessions racing on this counter; HOLDLOCK extends it to a key range so
    /// the day's first two payouts cannot both decide the row does not exist and both insert. Exactly
    /// the lock <c>WalletService</c> takes on a balance, for exactly the same reason.
    /// </summary>
    private async Task AccrueSignalAsync(
        Guid userId,
        string kind,
        int paidCount,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var day = nowUtc.Date;

        var existing = await _dbContext.Database
            .SqlQueryRaw<int>(
                "SELECT [PaidCount] AS [Value] FROM [DailySignalLedger] WITH (UPDLOCK, HOLDLOCK) "
                + "WHERE [UserId] = {0} AND [SignalKind] = {1} AND [DayUtc] = {2}",
                userId, kind, day)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE [DailySignalLedger] SET [PaidCount] = [PaidCount] + {0}, [UpdatedAtUtc] = {1} "
                + "WHERE [UserId] = {2} AND [SignalKind] = {3} AND [DayUtc] = {4}",
                [paidCount, nowUtc, userId, kind, day],
                cancellationToken);

            return;
        }

        await _dbContext.Database.ExecuteSqlRawAsync(
            "INSERT INTO [DailySignalLedger] ([UserId], [SignalKind], [DayUtc], [PaidCount], [UpdatedAtUtc]) "
            + "VALUES ({0}, {1}, {2}, {3}, {4})",
            [userId, kind, day, paidCount, nowUtc],
            cancellationToken);
    }

    /// <summary>
    /// Remembers every cap that fired and reports them narrowest-first, so the client is told the
    /// most specific true reason rather than whichever one happened to fire first.
    /// </summary>
    private sealed class CapLadder
    {
        private static readonly string[] Precedence =
        [
            "signal_rate_limit",
            "signal_daily_limit",
            "daily_coin_limit",
            "signal_limit"
        ];

        private readonly List<string> _reached = [];

        public void Reached(string message)
        {
            if (!_reached.Contains(message))
                _reached.Add(message);
        }

        public IReadOnlyList<string> Messages =>
            _reached.OrderBy(m => Array.IndexOf(Precedence, m)).ToList();
    }
}
