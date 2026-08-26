using Share7.Application.Economy.Models;
using Share7.Application.Objectives.Models;
using Share7.Application.Progression.Models;

namespace Share7.Application.Users.Models;

/// <summary>
/// Everything the admin console shows on one account.
/// </summary>
/// <remarks>
/// <para>
/// These exist because every read path for a player's own state is <c>/me</c>-scoped at the
/// controller — <c>ProgressionController.GetMine</c>, <c>CommerceController.GetEntitlements</c>,
/// and so on all resolve the caller from <c>ICurrentUserService</c>. The *services* underneath
/// already take a <c>userId</c>, so nothing new is computed here: these are admin-scoped
/// projections over calls that already existed.
/// </para>
/// <para>
/// Read-only, deliberately. Nothing in this file mutates an account. The one write the console
/// performs against another user is <c>POST /api/admin/entitlements</c>, which predates this work;
/// crediting an arbitrary user's wallet stays closed — see the note on
/// <c>CurrenciesController.Grant</c>, which restricts that route to the caller's own account on
/// purpose.
/// </para>
/// <para>
/// The users are children. These payloads are for an operator answering "what happened to this
/// account", not a telemetry feed — nothing here is aggregated across users, and none of it should
/// be forwarded to a system that does.
/// </para>
/// </remarks>
public class AdminUserDetailDto
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? FullName { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public int? Age { get; init; }
    public Guid? GradeId { get; init; }
    public string? GradeName { get; init; }
    public Guid? PreferredLanguageId { get; init; }
    public string? PreferredLanguageCode { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public bool IsProfileComplete { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }

    /// <summary>Latest run start — the closest thing the schema has to a last-seen timestamp.</summary>
    public DateTime? LastSeenAtUtc { get; init; }

    // ── activity counters ──
    // Counted rather than listed: the lists have their own endpoints, and a detail
    // header wants "42 runs" not forty-two rows.
    public int RunCount { get; init; }
    public int FlaggedRunCount { get; init; }
    public int EntitlementCount { get; init; }
    public int PurchaseCount { get; init; }
    public int LessonsCompleted { get; init; }
}

// ---------------------------------------------------------------------------
// Wallet
// ---------------------------------------------------------------------------

public class AdminUserWalletDto
{
    public IReadOnlyList<BalanceDto> Balances { get; init; } = [];

    /// <summary>Most recent first. Bounded by the <c>take</c> the caller asked for.</summary>
    public IReadOnlyList<AdminLedgerEntryDto> Recent { get; init; } = [];

    /// <summary>Total ledger rows for the account, so the console can say "showing 50 of 900".</summary>
    public int LedgerCount { get; init; }
}

/// <summary>
/// One movement of currency.
/// </summary>
/// <remarks>
/// <see cref="BalanceAfter"/> is included because it is what makes the ledger auditable: a
/// disputed balance is settled by reading down the column, not by re-adding the amounts.
/// </remarks>
public class AdminLedgerEntryDto
{
    public long Id { get; init; }
    public Guid CurrencyId { get; init; }
    public string Currency { get; init; } = string.Empty;

    /// <summary>Signed. Negative is a debit.</summary>
    public long Amount { get; init; }

    public long BalanceAfter { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

// ---------------------------------------------------------------------------
// Progression
// ---------------------------------------------------------------------------

public class AdminUserProgressionDto
{
    public PlayerLevelDto Level { get; init; } = new();
    public StreakDto Streak { get; init; } = new();

    /// <summary>
    /// The same objective list the player sees, resolved for this account — including
    /// progress, state and whether a reward is sitting unclaimed.
    /// </summary>
    public IReadOnlyList<ObjectiveDto> Objectives { get; init; } = [];
}

// ---------------------------------------------------------------------------
// Entitlements
// ---------------------------------------------------------------------------

/// <summary>
/// An owned product, joined to the product so the console can render a key rather than a GUID.
/// </summary>
public class AdminUserEntitlementDto
{
    public Guid EntitlementId { get; init; }
    public Guid ProductId { get; init; }
    public string ProductKey { get; init; } = string.Empty;
    public string KindName { get; init; } = string.Empty;
    public bool ProductActive { get; init; }
    public DateTime GrantedAtUtc { get; init; }

    /// <summary>How they came to own it — Purchase, AdminGrant, Reward.</summary>
    public string Source { get; init; } = string.Empty;

    public string? SourceId { get; init; }
}

// ---------------------------------------------------------------------------
// Runs
// ---------------------------------------------------------------------------

/// <summary>
/// A run summary for one account's history.
/// </summary>
/// <remarks>
/// Lighter than <c>RunAdminDto</c> on purpose: that carries every collected signal and every
/// payout line, which is right for the cheat-review drawer and wrong for a fifty-row history.
/// <c>GET /api/admin/runs/{runId}</c> is still the way to open one.
/// </remarks>
public class AdminUserRunDto
{
    public Guid RunId { get; init; }
    public Guid GameId { get; init; }
    public string State { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; init; }
    public int DurationMs { get; init; }
    public bool IsFlagged { get; init; }
    public string? FlagReason { get; init; }
    public bool Reviewed { get; init; }

    /// <summary>Net currency this run actually paid, summed across its payout lines.</summary>
    public long NetPaid { get; init; }
}
