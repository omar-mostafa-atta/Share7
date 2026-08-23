using Microsoft.EntityFrameworkCore;
using Share7.Domain.Entities;
using Share7.Domain.Leaderboards;
using Share7.Domain.Multiplayer;
using Share7.Domain.Progress;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Users;

/// <summary>
/// The single definition of what "everything this account owns" means. Both the admin delete and
/// the user's own delete go through here, so the two can never drift apart.
/// </summary>
public static class UserOwnedData
{
    /// <summary>
    /// User-keyed tables with **no cascading foreign key** to <c>AspNetUsers</c>. Nothing removes
    /// these automatically, so they have to be deleted explicitly or they outlive the account as
    /// orphans.
    /// <para>
    /// Tables that <i>do</i> have a cascading FK are deliberately absent — the database already
    /// handles them, and listing them here would imply the manual sweep is load-bearing when it
    /// is not.
    /// </para>
    /// <para>
    /// **Adding a user-keyed table means adding it here.** <c>AccountDeletionCoverageTests</c>
    /// walks the EF model and fails when a table carrying a <c>UserId</c> is neither in this list
    /// nor protected by a cascade, so forgetting is a failing test rather than a support ticket.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<Type> ManuallyPurged =
    [
        typeof(RefreshToken),
        typeof(StudentProfile),
        typeof(UserQuestionProgress),
        typeof(UserLessonProgress),
        typeof(UserNodeUnlock),

        // Listed rather than cascaded, and not by preference. A cascade from AspNetUsers already
        // reaches this table the long way round — user → MultiplayerSessions (they hosted) →
        // MultiplayerSessionPlayers — and SQL Server refuses a second cascade path into the same
        // table. So memberships in *other people's* sessions are removed here instead. The purge
        // runs before the user row goes, which is also what keeps the NoAction FK satisfied.
        typeof(MultiplayerSessionPlayer),

        // Leaderboard standings, for the same structural reason: the cascade already arrives via
        // the cycle's board, and SQL Server allows only one path.
        //
        // **A child's competitive history is deleted, not anonymised.** An anonymised row is still
        // that child's record — it keeps their score, their timing and their position among their
        // classmates, all of which are re-identifiable to anyone who was on the board at the time.
        // Keeping it would also mean this platform holds a permanent ranking of a nine-year-old
        // who asked to be forgotten. The currency they were paid stays in the ledger, because that
        // is the economy's audit trail rather than a record about them.
        //
        // Removing entries mid-cycle leaves gaps in the ranks until the next reindex, which is
        // correct: rank 4 disappearing does not promote rank 5 to fourth place retroactively.
        typeof(LeaderboardEntry),
        typeof(LeaderboardSettlement)
    ];

    /// <summary>
    /// Deletes every row in <see cref="ManuallyPurged"/> belonging to the user, and reports how
    /// many rows went.
    /// <para>
    /// Driven off the list rather than hand-written per table, so a type added to the list is
    /// purged without a second edit — the failure mode where the list and the code disagree
    /// cannot happen.
    /// </para>
    /// </summary>
    public static async Task<int> PurgeAsync(
        ApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var deleted = 0;

        foreach (var clrType in ManuallyPurged)
        {
            // Table and column names come from the EF model, never from input, so composing them
            // into SQL is safe. The user id stays a parameter.
            var entityType = context.Model.FindEntityType(clrType)
                ?? throw new InvalidOperationException($"{clrType.Name} is not part of the EF model.");

            var schema = entityType.GetSchema() ?? "dbo";
            var table = entityType.GetTableName()
                ?? throw new InvalidOperationException($"{clrType.Name} is not mapped to a table.");

            deleted += await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM [{schema}].[{table}] WHERE [UserId] = {{0}}",
                [userId],
                cancellationToken);
        }

        return deleted;
    }
}
