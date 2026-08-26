using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Share7.Application.Common.Models;
using Share7.Application.Telemetry.Interfaces;
using Share7.Application.Telemetry.Models;
using Share7.Domain.Runs;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Telemetry;

/// <summary>
/// Everything that ever happened to one child, from every table that recorded any of it, in one
/// order.
/// <para>
/// **A read across the authoritative tables, never a copy of them.** The ledger already knows every
/// grant; the run table already knows every settlement; telemetry already knows every screen. What
/// was missing was a reader that put them on one line together, and that is all this is. A second
/// write path that mirrored these into a "timeline" table would be a second answer to "what was
/// this child given", and one of the two would be wrong. See <c>Docs/AnalyticsArchitecture.md</c>
/// → Rule 2.
/// </para>
/// <para>
/// **Each source is queried on its own index and merged in memory.** Seven ordered lists of at most
/// <c>limit</c> rows each, merged and truncated, is a few hundred rows — and every one of those
/// queries is a seek. The alternative, a SQL <c>UNION ALL</c> across heterogeneous tables, cannot
/// use any of their indexes for the ordering and would sort the whole account's history to return a
/// page of it.
/// </para>
/// </summary>
public class UserTimelineService : IUserTimelineService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly TelemetryOptions _options;

    public UserTimelineService(ApplicationDbContext dbContext, IOptions<TelemetryOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    // ---- the 360 header -------------------------------------------------------------------------

    public async Task<ServiceResult<UserAnalyticsProfileDto>> GetProfileAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Set<ApplicationUser>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.UserName })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
            return ServiceResult<UserAnalyticsProfileDto>.NotFound("No such account.");

        var lifecycle = await _dbContext.TelemetryUserLifecycle
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);

        var recent = await _dbContext.TelemetryUserDays
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.DayUtc)
            .Take(42)
            .Select(d => new UserActivityDayDto
            {
                DayUtc = d.DayUtc,
                Sessions = d.SessionCount,
                PlaySeconds = d.PlaySeconds,
                Events = d.EventCount,
                Runs = d.RunCount,
                Attempts = d.AttemptCount
            })
            .ToListAsync(cancellationToken);

        // Counted from the authoritative tables, not from TelemetryUserDay. A child who played
        // offline for a week has runs the client has not reported yet, and a support question that
        // says "no runs" when the run table has forty is worse than no answer.
        var runStats = await _dbContext.Runs
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Flagged = g.Count(r => r.IsFlagged) })
            .FirstOrDefaultAsync(cancellationToken);

        var attemptCount = await _dbContext.GameResults
            .AsNoTracking()
            .CountAsync(r => r.UserId == userId, cancellationToken);

        var purchaseCount = await _dbContext.PurchaseTransactions
            .AsNoTracking()
            .CountAsync(p => p.UserId == userId, cancellationToken);

        var entitlementCount = await _dbContext.Entitlements
            .AsNoTracking()
            .CountAsync(e => e.UserId == userId, cancellationToken);

        var balances = await _dbContext.UserCurrencyBalances
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Select(b => new UserBalanceDto
            {
                CurrencyId = b.CurrencyId,
                Code = b.Currency!.Key,
                Balance = b.Amount
            })
            .ToListAsync(cancellationToken);

        // Lifetime in and out per currency, summed in the database. This is the number that
        // explains a balance — "you have 300" is not an answer to "where did it come from".
        var flow = await _dbContext.CurrencyLedgerEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .GroupBy(e => new { e.CurrencyId, Code = e.Currency!.Key })
            .Select(g => new UserCurrencyFlowDto
            {
                CurrencyId = g.Key.CurrencyId,
                Code = g.Key.Code,
                Earned = g.Where(e => e.Amount > 0).Sum(e => e.Amount),
                Spent = -g.Where(e => e.Amount < 0).Sum(e => e.Amount)
            })
            .ToListAsync(cancellationToken);

        return ServiceResult<UserAnalyticsProfileDto>.Success(new UserAnalyticsProfileDto
        {
            UserId = userId,
            UserName = user.UserName,
            FirstSeenAtUtc = lifecycle?.FirstSeenAtUtc,
            LastSeenAtUtc = lifecycle?.LastSeenAtUtc,
            CohortDayUtc = lifecycle?.CohortDayUtc,
            DayIndex = lifecycle is null
                ? null
                : Math.Max(0, (int)(DateTime.UtcNow.Date - lifecycle.CohortDayUtc).TotalDays),
            ActiveDays = recent.Count > 0 ? await ActiveDaysAsync(userId, cancellationToken) : 0,
            TotalSessions = lifecycle?.TotalSessions ?? 0,
            TotalEvents = lifecycle?.TotalEvents ?? 0,
            TotalPlaySeconds = lifecycle?.TotalPlaySeconds ?? 0,
            InstallAppVersion = lifecycle?.InstallAppVersion,
            InstallPlatform = lifecycle?.InstallPlatform,
            LastAppVersion = lifecycle?.LastAppVersion,
            LastPlatform = lifecycle?.LastPlatform,
            RunCount = runStats?.Total ?? 0,
            FlaggedRunCount = runStats?.Flagged ?? 0,
            AttemptCount = attemptCount,
            PurchaseCount = purchaseCount,
            EntitlementCount = entitlementCount,
            Balances = balances,
            CurrencyFlow = flow,
            RecentDays = recent
        });
    }

    private Task<int> ActiveDaysAsync(Guid userId, CancellationToken cancellationToken) =>
        _dbContext.TelemetryUserDays.AsNoTracking().CountAsync(d => d.UserId == userId, cancellationToken);

    // ---- the trace ------------------------------------------------------------------------------

    public async Task<ServiceResult<UserTimelineDto>> GetTimelineAsync(
        Guid userId,
        DateTime? beforeUtc,
        int limit,
        IReadOnlyList<TimelineSourceKind>? sources,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, _options.MaxTimelinePageSize);

        var before = beforeUtc ?? DateTime.UtcNow.AddYears(1);
        var wanted = sources is { Count: > 0 } ? sources.ToHashSet() : null;

        bool Want(TimelineSourceKind kind) => wanted is null || wanted.Contains(kind);

        var entries = new List<TimelineEntryDto>(limit * 4);

        if (Want(TimelineSourceKind.Telemetry))
            entries.AddRange(await TelemetryEntriesAsync(userId, before, limit, cancellationToken));

        if (Want(TimelineSourceKind.CurrencyLedger))
            entries.AddRange(await LedgerEntriesAsync(userId, before, limit, cancellationToken));

        if (Want(TimelineSourceKind.Reward))
            entries.AddRange(await RewardEntriesAsync(userId, before, limit, cancellationToken));

        if (Want(TimelineSourceKind.Purchase))
            entries.AddRange(await PurchaseEntriesAsync(userId, before, limit, cancellationToken));

        if (Want(TimelineSourceKind.Entitlement))
            entries.AddRange(await EntitlementEntriesAsync(userId, before, limit, cancellationToken));

        if (Want(TimelineSourceKind.Run))
            entries.AddRange(await RunEntriesAsync(userId, before, limit, cancellationToken));

        if (Want(TimelineSourceKind.Attempt))
            entries.AddRange(await AttemptEntriesAsync(userId, before, limit, cancellationToken));

        var page = entries
            .OrderByDescending(e => e.AtUtc)

            // A stable tiebreak, because several sources legitimately stamp the same instant — a run
            // settles, the ledger moves and the reward is written inside one transaction. Without
            // it the order of those three rows changes between two requests for the same page, and
            // the cursor below can then skip or repeat one.
            .ThenByDescending(e => e.Source)
            .ThenByDescending(e => e.RefId, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        // The cursor is the last entry's timestamp, not an offset. An offset into a merged list
        // shifts the moment any one source gets a new row, and the reader silently skips entries.
        DateTime? next = page.Count == limit ? page[^1].AtUtc : null;

        return ServiceResult<UserTimelineDto>.Success(new UserTimelineDto
        {
            UserId = userId,
            Entries = page,
            NextBeforeUtc = next
        });
    }

    private async Task<List<TimelineEntryDto>> TelemetryEntriesAsync(
        Guid userId, DateTime before, int limit, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.TelemetryEvents
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.ReceivedAtUtc < before)
            .OrderByDescending(e => e.ReceivedAtUtc)
            .Take(limit)
            .Select(e => new
            {
                e.Id, e.Name, e.OccurredAtUtc, e.ReceivedAtUtc, e.SessionId,
                e.GameId, e.RunId, e.ParamsJson, e.Category, e.IsUnregistered
            })
            .ToListAsync(cancellationToken);

        return rows.Select(e => new TimelineEntryDto
        {
            Source = TimelineSourceKind.Telemetry,

            // Ordered on the server's clock, not the client's. Two events a second apart on a
            // tablet whose clock drifts would otherwise interleave wrongly with the ledger rows
            // beside them — and the whole value of this view is that the order is trustworthy.
            AtUtc = e.ReceivedAtUtc,
            Kind = e.Name,
            Summary = DescribeEvent(e.Name, e.ParamsJson),
            RefId = e.Id.ToString(),
            GameId = e.GameId,
            RunId = e.RunId,
            SessionId = e.SessionId,
            Data = Flatten(e.ParamsJson, extra: e.IsUnregistered
                ? new Dictionary<string, string> { ["category"] = e.Category.ToString(), ["unregistered"] = "true" }
                : new Dictionary<string, string> { ["category"] = e.Category.ToString() })
        }).ToList();
    }

    private async Task<List<TimelineEntryDto>> LedgerEntriesAsync(
        Guid userId, DateTime before, int limit, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.CurrencyLedgerEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.CreatedAtUtc < before)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(limit)
            .Select(e => new
            {
                e.Id, e.Amount, e.BalanceAfter, e.TransactionType, e.SourceType,
                e.SourceId, e.IdempotencyKey, e.CreatedAtUtc, Code = e.Currency!.Key
            })
            .ToListAsync(cancellationToken);

        return rows.Select(e => new TimelineEntryDto
        {
            Source = TimelineSourceKind.CurrencyLedger,
            AtUtc = e.CreatedAtUtc,
            Kind = e.TransactionType.ToString(),
            Summary = $"{(e.Amount >= 0 ? "+" : "")}{e.Amount} {e.Code} — {e.TransactionType} " +
                      $"(balance {e.BalanceAfter})",
            RefId = e.Id.ToString(),
            Amount = e.Amount,
            CurrencyCode = e.Code,
            BalanceAfter = e.BalanceAfter,
            Data = new Dictionary<string, string>
            {
                ["source_type"] = e.SourceType.ToString(),
                ["source_id"] = e.SourceId ?? string.Empty,
                ["idempotency_key"] = e.IdempotencyKey ?? string.Empty
            }
        }).ToList();
    }

    private async Task<List<TimelineEntryDto>> RewardEntriesAsync(
        Guid userId, DateTime before, int limit, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.RewardTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.CreatedAtUtc < before)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(limit)
            .Select(t => new
            {
                t.Id, t.EventType, t.SourceType, t.SourceId, t.CreatedAtUtc,
                RuleKey = t.RewardRule!.Name,
                Lines = t.Lines.Select(l => new { l.Amount, Code = l.Currency!.Key }).ToList()
            })
            .ToListAsync(cancellationToken);

        return rows.Select(t => new TimelineEntryDto
        {
            Source = TimelineSourceKind.Reward,
            AtUtc = t.CreatedAtUtc,
            Kind = t.EventType.ToString(),
            Summary = t.Lines.Count == 0
                ? $"Reward '{t.RuleKey}' paid nothing"
                : $"Reward '{t.RuleKey}' paid " +
                  string.Join(", ", t.Lines.Select(l => $"{l.Amount} {l.Code}")),
            RefId = t.Id.ToString(),

            // The single-line case gets the amount fields; a multi-currency payout does not, because
            // one number cannot honestly represent two currencies and a console that picked the
            // first would quietly under-report the rest.
            Amount = t.Lines.Count == 1 ? t.Lines[0].Amount : null,
            CurrencyCode = t.Lines.Count == 1 ? t.Lines[0].Code : null,
            Data = new Dictionary<string, string>
            {
                ["rule"] = t.RuleKey,
                ["source_type"] = t.SourceType.ToString(),
                ["source_id"] = t.SourceId ?? string.Empty,
                ["lines"] = t.Lines.Count.ToString()
            }
        }).ToList();
    }

    private async Task<List<TimelineEntryDto>> PurchaseEntriesAsync(
        Guid userId, DateTime before, int limit, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.PurchaseTransactions
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CreatedAtUtc < before)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(limit)
            .Select(p => new
            {
                p.Id, p.OfferId, p.State, p.Price, p.FailureReasonKey, p.CreatedAtUtc,
                Code = p.Currency!.Key
            })
            .ToListAsync(cancellationToken);

        return rows.Select(p => new TimelineEntryDto
        {
            Source = TimelineSourceKind.Purchase,
            AtUtc = p.CreatedAtUtc,
            Kind = p.State.ToString(),

            // Refusals are on the trace too. "Why does this child think they were charged" is
            // exactly the question this view exists for, and a trace that showed only successes
            // could not answer it.
            Summary = $"Purchase {p.State} — {p.Price} {p.Code}" +
                      (p.FailureReasonKey is null ? "" : $" ({p.FailureReasonKey})"),
            RefId = p.Id.ToString(),
            Amount = -p.Price,
            CurrencyCode = p.Code,
            Data = new Dictionary<string, string>
            {
                ["offer_id"] = p.OfferId.ToString(),
                ["state"] = p.State.ToString(),
                ["failure"] = p.FailureReasonKey ?? string.Empty
            }
        }).ToList();
    }

    private async Task<List<TimelineEntryDto>> EntitlementEntriesAsync(
        Guid userId, DateTime before, int limit, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Entitlements
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.GrantedAtUtc < before)
            .OrderByDescending(e => e.GrantedAtUtc)
            .Take(limit)
            .Select(e => new
            {
                e.Id, e.ProductId, e.Source, e.SourceId, e.GrantedAtUtc,
                ProductKey = e.Product!.Key
            })
            .ToListAsync(cancellationToken);

        return rows.Select(e => new TimelineEntryDto
        {
            Source = TimelineSourceKind.Entitlement,
            AtUtc = e.GrantedAtUtc,
            Kind = e.Source.ToString(),
            Summary = $"Granted '{e.ProductKey}' via {e.Source}",
            RefId = e.Id.ToString(),
            Data = new Dictionary<string, string>
            {
                ["product_id"] = e.ProductId.ToString(),
                ["product_key"] = e.ProductKey,
                ["source_id"] = e.SourceId ?? string.Empty
            }
        }).ToList();
    }

    private async Task<List<TimelineEntryDto>> RunEntriesAsync(
        Guid userId, DateTime before, int limit, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Runs
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.StartedAtUtc < before)
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(limit)
            .Select(r => new
            {
                r.Id, r.GameId, r.State, r.Outcome, r.DurationMs, r.IsFlagged, r.FlagReason,
                r.CapReached, r.CapMessage, r.StartedAtUtc, r.EndedAtUtc, r.SessionId,
                Payouts = r.Payouts.Select(p => new { p.Source, p.NetAmount, Code = p.Currency!.Key }).ToList()
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TimelineEntryDto
        {
            Source = TimelineSourceKind.Run,

            // The settlement time when there is one, because that is when the run affected the
            // account. An unsettled run is placed at its start, which is the only time it has.
            AtUtc = r.EndedAtUtc ?? r.StartedAtUtc,
            Kind = r.State.ToString(),
            Summary = $"Run {r.State}/{r.Outcome} — {r.DurationMs / 1000}s" +
                      (r.Payouts.Count > 0
                          ? $", paid {string.Join(", ", r.Payouts.Select(p => $"{p.NetAmount} {p.Code}"))}"
                          : ", no payout") +
                      (r.CapReached ? $" [capped: {r.CapMessage}]" : "") +
                      (r.IsFlagged ? $" [flagged: {r.FlagReason}]" : ""),
            RefId = r.Id.ToString(),
            GameId = r.GameId,
            RunId = r.Id,
            Data = new Dictionary<string, string>
            {
                ["state"] = r.State.ToString(),
                ["outcome"] = r.Outcome.ToString(),
                ["duration_ms"] = r.DurationMs.ToString(),
                ["flagged"] = r.IsFlagged ? "true" : "false",
                ["flag_reason"] = r.FlagReason ?? string.Empty,
                ["cap_reached"] = r.CapReached ? "true" : "false",
                ["cap_message"] = r.CapMessage ?? string.Empty,
                ["multiplayer_session"] = r.SessionId?.ToString() ?? string.Empty
            }
        }).ToList();
    }

    private async Task<List<TimelineEntryDto>> AttemptEntriesAsync(
        Guid userId, DateTime before, int limit, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.GameResults
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.CreatedAtUtc < before)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit)
            .Select(r => new
            {
                r.Id, r.GameId, r.Metric, r.Value, r.Scope, r.SourceType, r.SourceId,
                r.IsFlagged, r.FlagReason, r.CreatedAtUtc, r.OccurredAtUtc
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TimelineEntryDto
        {
            Source = TimelineSourceKind.Attempt,
            AtUtc = r.CreatedAtUtc,
            Kind = r.SourceType.ToString(),
            Summary = $"Result {r.Metric} = {r.Value}" +
                      (r.Scope is null ? "" : $" ({r.Scope})") +
                      (r.IsFlagged ? $" [flagged: {r.FlagReason}]" : ""),
            RefId = r.Id.ToString(),
            GameId = r.GameId,
            Data = new Dictionary<string, string>
            {
                ["metric"] = r.Metric,
                ["value"] = r.Value.ToString(),
                ["scope"] = r.Scope ?? string.Empty,
                ["source_id"] = r.SourceId.ToString(),
                ["occurred_at"] = r.OccurredAtUtc.ToString("O"),
                ["flagged"] = r.IsFlagged ? "true" : "false"
            }
        }).ToList();
    }

    // ---- payload rendering ----------------------------------------------------------------------

    /// <summary>
    /// One line describing an event, assembled server-side.
    /// <para>
    /// Here rather than in the console so every reader of the trace — the React panel, a support
    /// export, whatever comes next — renders the same sentence. Two consumers formatting the same
    /// row differently is how two people end up describing the same incident incompatibly.
    /// </para>
    /// </summary>
    private static string DescribeEvent(string name, string paramsJson)
    {
        var parts = Flatten(paramsJson, null);

        if (parts.Count == 0) return name;

        // Four is enough to recognise what happened and short enough to read in a table row. The
        // rest are still in Data for whoever expands the entry.
        var shown = parts.Take(4).Select(p => $"{p.Key}={p.Value}");

        return $"{name} ({string.Join(", ", shown)})";
    }

    private static Dictionary<string, string> Flatten(
        string? paramsJson, Dictionary<string, string>? extra)
    {
        var result = extra is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(extra, StringComparer.Ordinal);

        if (string.IsNullOrEmpty(paramsJson) || paramsJson == "{}") return result;

        try
        {
            using var document = JsonDocument.Parse(paramsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    _ => property.Value.GetRawText()
                };
            }
        }
        catch (JsonException)
        {
            // A payload written by a build from two years ago must not break the page. The entry
            // still renders with its name, time and source — which is most of what the trace is for.
            result["_malformed"] = "true";
        }

        return result;
    }
}
