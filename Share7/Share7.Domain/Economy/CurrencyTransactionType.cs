namespace Share7.Domain.Economy;

/// <summary>
/// Why a balance moved. Stored as text (<c>LESSON_REWARD</c>, <c>ADMIN_GRANT</c>) rather than an
/// integer, so the audit trail is readable in a SQL window and adding a value never needs a
/// migration.
/// <para>
/// Deliberately broader than what the code emits today. Most of these have no producer yet —
/// they exist so that a later achievements or events module records against the same ledger
/// instead of growing a parallel one, which is how an economy turns into several disagreeing
/// coin counters.
/// </para>
/// </summary>
public enum CurrencyTransactionType
{
    Unknown = 0,

    // ---- credits ----
    LessonReward,
    GameReward,
    AchievementReward,
    DailyReward,
    EventReward,
    LeaderboardReward,
    AdminGrant,

    // ---- debits ----
    Purchase,

    // ---- corrections: never mutate history, always a new compensating entry ----
    AdminAdjustment,
    Correction,
    Refund,
    Reversal
}

/// <summary>
/// Which subsystem produced the entry, kept separate from <see cref="CurrencyTransactionType"/>
/// so <c>SourceId</c> can be interpreted without guessing. A <c>LessonReward</c> sourced from
/// <c>ProgressAttempt</c> points at a lesson; the same type sourced from <c>Admin</c> would not.
/// </summary>
public enum LedgerSourceType
{
    Unknown = 0,
    ProgressAttempt,
    Purchase,
    Admin,
    System
}
