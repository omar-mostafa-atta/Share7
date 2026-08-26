namespace Share7.Application.Admin.Models;

/// <summary>
/// Platform counters for the console's landing page.
/// </summary>
/// <remarks>
/// This type exists so the dashboard is one request instead of thirteen.
///
/// Every figure below was already reachable — as a full list endpoint. Drawing the
/// landing page from those would mean fetching every game, every offer, every
/// objective, every reward rule and every board on each visit, to display their
/// lengths. That makes the admin console the single most expensive client of its
/// own API, and it gets worse as the platform grows, which is precisely backwards
/// for a screen whose job is to say whether things are healthy.
///
/// Counters only. Nothing here identifies a user, and nothing here is a metric the
/// product team should be reading — see the crash-reporting rule in CLAUDE.md for
/// why diagnostics and analytics stay separate systems.
/// </remarks>
public class AdminOverviewDto
{
    // ── audience ──
    public int Users { get; init; }
    public int UsersAddedLast7Days { get; init; }

    // ── content ──
    public int Games { get; init; }
    public int ActiveGames { get; init; }
    public int Grades { get; init; }
    public int Lessons { get; init; }

    /// <summary>
    /// Lessons with at least one published question set. The gap between this and
    /// <see cref="Lessons"/> is the authoring backlog, and it is the single most
    /// useful number on the page: a lesson with no questions cannot be played.
    /// </summary>
    public int LessonsWithQuestions { get; init; }

    public int Questions { get; init; }

    // ── economy ──
    public int Currencies { get; init; }
    public int Offers { get; init; }
    public int ActiveOffers { get; init; }
    public int Products { get; init; }
    public int RewardRules { get; init; }
    public int EnabledRewardRules { get; init; }
    public int SignalValuations { get; init; }

    // ── engagement ──
    public int Objectives { get; init; }
    public int ActiveObjectives { get; init; }
    public int Boards { get; init; }
    public int OpenCycles { get; init; }

    // ── operations ──
    /// <summary>Sessions in a state that is still running, by the server's clock.</summary>
    public int LiveSessions { get; init; }

    public int FlaggedRuns { get; init; }
    public int FlaggedResults { get; init; }
    public int RunsLast24Hours { get; init; }

    /// <summary>
    /// Included so the console can label its figures with the server's clock rather
    /// than the browser's. They disagree often enough to matter when someone is
    /// deciding whether a cycle has closed.
    /// </summary>
    public DateTime ServerTimeUtc { get; init; }
}
