namespace Share7.Application.Telemetry.Models;

/// <summary>
/// Which table an entry on the trace came from.
/// <para>
/// **The list is the design.** Everything but <see cref="Telemetry"/> is an authoritative record
/// the platform already kept — the trace reads them, it does not copy them. Adding a source is a
/// member here and a query in <c>IUserTimelineService</c>; it is never a new write path, because a
/// second copy of a ledger is a second answer to "what was this child given" and one of them will
/// be wrong. See <c>Docs/AnalyticsArchitecture.md</c> → Rule 2.
/// </para>
/// </summary>
public enum TimelineSourceKind
{
    /// <summary>A behavioural or operational event the client reported.</summary>
    Telemetry = 0,

    /// <summary>A row of <c>CurrencyLedgerEntries</c> — every credit and debit, with the balance after.</summary>
    CurrencyLedger = 1,

    /// <summary>A <c>RewardTransaction</c> — which rule paid, and why.</summary>
    Reward = 2,

    /// <summary>A <c>PurchaseTransaction</c>, including the refused ones. A refusal is part of the trace.</summary>
    Purchase = 3,

    /// <summary>An <c>Entitlement</c> grant — what the account came to own, and from where.</summary>
    Entitlement = 4,

    /// <summary>A <c>Run</c> — started, settled, expired; flagged or not.</summary>
    Run = 5,

    /// <summary>A <c>GameResult</c> — a graded attempt, scored against the server's own answer key.</summary>
    Attempt = 6
}

/// <summary>
/// One thing that happened to one account, from whichever table recorded it.
/// <para>
/// Flattened to a common shape on purpose: the question the trace answers is "what happened to
/// this child, in order", and an answer that makes the reader switch schemas every third row does
/// not answer it. The typed detail is still there in <see cref="Data"/> for whoever needs it.
/// </para>
/// </summary>
public class TimelineEntryDto
{
    public TimelineSourceKind Source { get; init; }

    /// <summary>
    /// When it happened, on the most trustworthy clock available for that source — the server's,
    /// for everything except a telemetry event's <c>OccurredAtUtc</c>, which is the client's
    /// already clamped into the backlog window.
    /// </summary>
    public DateTime AtUtc { get; init; }

    /// <summary>Stable machine token — the event name, or the transaction type. Never prose.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>One line for a human, assembled server-side so every console renders it the same.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>The row's own id, so the console can link back to the record itself.</summary>
    public string? RefId { get; init; }

    public Guid? GameId { get; init; }

    public Guid? RunId { get; init; }

    /// <summary>The client session, when the source has one. Lets the console group a trace into visits.</summary>
    public Guid? SessionId { get; init; }

    /// <summary>
    /// Signed currency movement, when this entry moved a balance. **Only the ledger and the
    /// reward lines ever set it** — a telemetry event never carries an amount, which is what keeps
    /// the trace from appearing to double-count a grant it merely described.
    /// </summary>
    public long? Amount { get; init; }

    public string? CurrencyCode { get; init; }

    /// <summary>The balance immediately after, straight off the ledger row. Only the ledger sets it.</summary>
    public long? BalanceAfter { get; init; }

    /// <summary>Source-specific fields, already flattened to strings for display.</summary>
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
}

/// <summary>One page of the trace, newest first.</summary>
public class UserTimelineDto
{
    public Guid UserId { get; init; }
    public IReadOnlyList<TimelineEntryDto> Entries { get; init; } = [];

    /// <summary>
    /// Pass as <c>before</c> to fetch the next page. Null when the page reached the beginning of
    /// the range.
    /// <para>
    /// A timestamp cursor rather than an offset, because the trace merges seven independently
    /// ordered sources: an offset into a merged list shifts the moment any source gets a new row,
    /// and the reader silently skips entries.
    /// </para>
    /// </summary>
    public DateTime? NextBeforeUtc { get; init; }
}

/// <summary>
/// The header above the trace — who this account is and what it has done, in one row of facts.
/// <para>
/// Assembled from the rollups and the ledgers rather than the raw stream, so it stays a
/// primary-key read no matter how many events the account has behind it.
/// </para>
/// </summary>
public class UserAnalyticsProfileDto
{
    public Guid UserId { get; init; }
    public string? UserName { get; init; }

    /// <summary>Null when the account has never sent telemetry — a real state, not an error.</summary>
    public DateTime? FirstSeenAtUtc { get; init; }
    public DateTime? LastSeenAtUtc { get; init; }
    public DateTime? CohortDayUtc { get; init; }

    /// <summary>Days since install. The x-axis position this account occupies on the retention curve.</summary>
    public int? DayIndex { get; init; }

    public int ActiveDays { get; init; }
    public int TotalSessions { get; init; }
    public long TotalEvents { get; init; }
    public long TotalPlaySeconds { get; init; }

    public string? InstallAppVersion { get; init; }
    public string? InstallPlatform { get; init; }
    public string? LastAppVersion { get; init; }
    public string? LastPlatform { get; init; }

    /// <summary>Runs ever started, from <c>Runs</c> — the authoritative count, not the reported one.</summary>
    public int RunCount { get; init; }

    /// <summary>Runs that were flagged. A number worth seeing next to the trace it explains.</summary>
    public int FlaggedRunCount { get; init; }

    public int AttemptCount { get; init; }

    public int PurchaseCount { get; init; }

    public int EntitlementCount { get; init; }

    /// <summary>Live balances, straight from <c>UserCurrencyBalances</c>.</summary>
    public IReadOnlyList<UserBalanceDto> Balances { get; init; } = [];

    /// <summary>Lifetime credited and debited per currency, summed from the ledger.</summary>
    public IReadOnlyList<UserCurrencyFlowDto> CurrencyFlow { get; init; } = [];

    /// <summary>Daily activity for the last few weeks — the sparkline over the header.</summary>
    public IReadOnlyList<UserActivityDayDto> RecentDays { get; init; } = [];
}

public class UserBalanceDto
{
    public Guid CurrencyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public long Balance { get; init; }
}

public class UserCurrencyFlowDto
{
    public Guid CurrencyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public long Earned { get; init; }
    public long Spent { get; init; }
}

public class UserActivityDayDto
{
    public DateTime DayUtc { get; init; }
    public int Sessions { get; init; }
    public int PlaySeconds { get; init; }
    public int Events { get; init; }
    public int Runs { get; init; }
    public int Attempts { get; init; }
}
