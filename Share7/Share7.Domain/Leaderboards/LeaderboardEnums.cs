namespace Share7.Domain.Leaderboards;

/// <summary>
/// Which slice of players a board ranks within. Stored as text on the board row so the set can
/// grow without a migration.
/// <para>
/// **Only <see cref="All"/> and <see cref="Grade"/> are implemented**, because they are the only
/// two the database can currently answer. <see cref="School"/>, <see cref="Class"/> and
/// <see cref="Friends"/> are declared so board authoring and the wire format do not have to change
/// when enrolment lands, and every one of them is refused at authoring time until it does — a
/// cohort that silently ranks nobody is worse than one that refuses to be created.
/// </para>
/// </summary>
public enum LeaderboardCohort
{
    /// <summary>Everyone on the board. Always available.</summary>
    All = 0,

    /// <summary>Same grade as the caller, from <c>StudentProfile.GradeId</c>.</summary>
    Grade = 1,

    /// <summary>Not implemented — no school relation exists in the schema.</summary>
    School = 2,

    /// <summary>Not implemented — no class or enrolment relation exists in the schema.</summary>
    Class = 3,

    /// <summary>Not implemented — no social graph exists.</summary>
    Friends = 4,

    /// <summary>Not implemented — no country is recorded on the profile.</summary>
    Country = 5
}

/// <summary>How often a board's window rolls over.</summary>
public enum LeaderboardPeriod
{
    /// <summary>One cycle that never ends.</summary>
    AllTime = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,

    /// <summary>Authored bounds, no recurrence.</summary>
    Event = 4
}

/// <summary>
/// Which direction wins. <see cref="Desc"/> for points and distance; <see cref="Asc"/> for
/// times, where the smaller number is the better result.
/// </summary>
public enum LeaderboardSortDirection
{
    Desc = 0,
    Asc = 1
}

/// <summary>
/// How repeated results for one player combine into their single ranked value.
/// </summary>
public enum LeaderboardAggregation
{
    /// <summary>
    /// Keep the better result. A worse one is a **no-op, never a demotion** — the alternative
    /// makes the winning strategy "stop playing once you are ahead".
    /// </summary>
    Best = 0,

    /// <summary>Add every result. Used for counting metrics like lessons completed.</summary>
    Sum = 1,

    /// <summary>Overwrite with the most recent result.</summary>
    Last = 2
}

/// <summary>
/// Where a cycle is in its life. Ranks only move in <see cref="Open"/>.
/// </summary>
public enum LeaderboardCycleState
{
    /// <summary>Visible in listings, readable and empty, accepts no projection.</summary>
    Scheduled = 0,

    /// <summary>Projecting. The only state in which ranks change.</summary>
    Open = 1,

    /// <summary>
    /// Projection stopped and ranks frozen, still readable. Exists so settlement is never racing
    /// the last second of play.
    /// </summary>
    Closed = 2,

    /// <summary>Final ranks written and rewards issued. Immutable, and cacheable forever.</summary>
    Settled = 3
}

/// <summary>What produced a <see cref="GameResult"/>, so <c>SourceId</c> can be read without guessing.</summary>
public enum GameResultSource
{
    /// <summary>A graded lesson attempt. <c>SourceId</c> is the lesson.</summary>
    Attempt = 0,

    /// <summary>A multiplayer session outcome. <c>SourceId</c> is the session.</summary>
    Session = 1,

    /// <summary>An operator correction or backfill.</summary>
    Admin = 2
}
