using Microsoft.EntityFrameworkCore;
using Share7.Application.Admin.Interfaces;
using Share7.Application.Admin.Models;
using Share7.Domain.Commerce;
using Share7.Domain.Leaderboards;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Admin;

/// <summary>
/// Counts the platform for the console's landing page.
/// </summary>
public class AdminOverviewService : IAdminOverviewService
{
    private readonly ApplicationDbContext _dbContext;

    public AdminOverviewService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Session states that mean "this session is still running".</summary>
    /// <remarks>
    /// Written as the set of live states rather than as <c>!= Closed</c>. The enum has
    /// seven members and two of them (<c>Closing</c>, <c>Ending</c>) are teardown, so
    /// negating the terminal one would report a session that is already going away as
    /// live. Naming the live set means a new state added later defaults to "not live",
    /// which is the safe direction for a number an operator reads as "load right now".
    /// </remarks>
    private static readonly MultiplayerSessionState[] LiveSessionStates =
    [
        MultiplayerSessionState.Creating,
        MultiplayerSessionState.Created,
        MultiplayerSessionState.Starting,
        MultiplayerSessionState.Running
    ];

    public async Task<AdminOverviewDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var dayAgo = now.AddDays(-1);

        // Every count is its own round-trip. That is deliberate rather than lazy: the
        // alternative is one query with a dozen correlated sub-selects, which SQL Server
        // plans as a dozen scans anyway and which no one can read six months later.
        // These are all indexed COUNTs against a single connection, and the page is not
        // on a hot path.
        //
        // AsNoTracking is not needed — none of these materialise entities.

        var users = await _dbContext.Users.CountAsync(cancellationToken);

        var usersAddedLast7Days = await _dbContext.Users
            .CountAsync(u => u.CreatedAt >= weekAgo, cancellationToken);

        var games = await _dbContext.Games.CountAsync(cancellationToken);
        var activeGames = await _dbContext.Games.CountAsync(g => g.IsActive, cancellationToken);

        var grades = await _dbContext.Grades.CountAsync(cancellationToken);
        var lessons = await _dbContext.Lessons.CountAsync(cancellationToken);

        // A lesson counts as covered once any language has a published set. Counting
        // distinct LessonId rather than rows: the same lesson published in English and
        // Arabic is one covered lesson, and summing the sets would overstate coverage
        // by exactly the number of translated lessons.
        var lessonsWithQuestions = await _dbContext.LessonQuestionSets
            .Select(s => s.LessonId)
            .Distinct()
            .CountAsync(cancellationToken);

        var questions = await _dbContext.Questions.CountAsync(cancellationToken);

        var currencies = await _dbContext.Currencies.CountAsync(cancellationToken);

        var offers = await _dbContext.Offers.CountAsync(cancellationToken);

        // "Active" means a player could buy it right now: on sale, and either open-ended
        // or not yet expired. An offer marked Available with a past expiry is not active,
        // and reporting it as such is how a dead promotion goes unnoticed for a week.
        var activeOffers = await _dbContext.Offers
            .CountAsync(
                o => o.Availability == OfferAvailability.Available
                     && (o.ExpiresAtUtc == null || o.ExpiresAtUtc > now),
                cancellationToken);

        var products = await _dbContext.Products.CountAsync(cancellationToken);

        var objectives = await _dbContext.Objectives.CountAsync(cancellationToken);

        // Same reasoning as offers: active means live now, so the availability window
        // is part of the test rather than the IsActive flag alone.
        var activeObjectives = await _dbContext.Objectives
            .CountAsync(
                o => o.IsActive
                     && (o.AvailableFromUtc == null || o.AvailableFromUtc <= now)
                     && (o.AvailableToUtc == null || o.AvailableToUtc > now),
                cancellationToken);

        var rewardRules = await _dbContext.RewardRules.CountAsync(cancellationToken);
        var enabledRewardRules = await _dbContext.RewardRules.CountAsync(r => r.Enabled, cancellationToken);

        var signalValuations = await _dbContext.SignalValuations.CountAsync(cancellationToken);

        var boards = await _dbContext.LeaderboardBoards.CountAsync(cancellationToken);

        var openCycles = await _dbContext.LeaderboardCycles
            .CountAsync(c => c.State == LeaderboardCycleState.Open, cancellationToken);

        var liveSessions = await _dbContext.MultiplayerSessions
            .CountAsync(s => LiveSessionStates.Contains(s.State), cancellationToken);

        // Unreviewed only. A run that was flagged and then cleared by a human is not
        // outstanding work, and leaving it in the count means the number never falls and
        // everyone learns to ignore it.
        var flaggedRuns = await _dbContext.Runs
            .CountAsync(r => r.IsFlagged && r.ReviewedAtUtc == null, cancellationToken);

        var flaggedResults = await _dbContext.GameResults
            .CountAsync(r => r.IsFlagged, cancellationToken);

        var runsLast24Hours = await _dbContext.Runs
            .CountAsync(r => r.StartedAtUtc >= dayAgo, cancellationToken);

        return new AdminOverviewDto
        {
            Users = users,
            UsersAddedLast7Days = usersAddedLast7Days,
            Games = games,
            ActiveGames = activeGames,
            Grades = grades,
            Lessons = lessons,
            LessonsWithQuestions = lessonsWithQuestions,
            Questions = questions,
            Currencies = currencies,
            Offers = offers,
            ActiveOffers = activeOffers,
            Products = products,
            Objectives = objectives,
            ActiveObjectives = activeObjectives,
            RewardRules = rewardRules,
            EnabledRewardRules = enabledRewardRules,
            SignalValuations = signalValuations,
            Boards = boards,
            OpenCycles = openCycles,
            LiveSessions = liveSessions,
            FlaggedRuns = flaggedRuns,
            FlaggedResults = flaggedResults,
            RunsLast24Hours = runsLast24Hours,
            ServerTimeUtc = now
        };
    }
}
