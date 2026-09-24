using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Share7.Domain.Entities;
using Share7.Domain.Evidence;
using Share7.Domain.Leaderboards;
using Share7.Domain.Measurement;
using Share7.Domain.Multiplayer;
using Share7.Domain.Play;
using Share7.Domain.Progress;
using Share7.Domain.Structure;
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

        // The educational evidence log. **Deleted, not anonymised** — a response carries the
        // timing, the ordering and the exact wrong answer a named child gave, which is
        // re-identifiable to anyone who knows what they were studying that week.
        //
        // Keyed by LearnerId rather than UserId, which is why the purge below resolves the column
        // from the model instead of assuming one name.
        //
        // **Phase 2 constraint, recorded here because this is where it will bite.** Item
        // statistics must be maintained as running aggregates rather than recomputed from these
        // rows, or deleting one child's evidence would silently shift every item's difficulty and
        // therefore every *other* child's measurements. See Docs/EducationalArchitecture.md §12.4.
        typeof(LearnerResponse),

        // The derived measurement layer, purged before the evidence it was derived from. All three
        // are rebuildable from the responses — which is exactly why deleting them costs nothing and
        // keeping them would be indefensible: a measurement is a claim about a named child, and a
        // verdict is a judgement about one.
        //
        // Observations would go anyway, by cascade from the responses. They are listed because the
        // guard reads this list, and a table that is only safe by accident is a table that stops
        // being safe the moment somebody changes a delete behaviour.
        typeof(MasteryVerdict),
        typeof(Domain.Measurement.Measurement),
        typeof(Observation),

        // Which curriculum a learner is following, and when. Keyed by LearnerId for the same
        // reason the responses are, and deleted rather than kept: "Preparatory One, Egyptian
        // national, September 2026" is a fact about a child, not about the curriculum.
        typeof(Enrollment),

        // Sittings, and what the platform was willing to say about a learner's examination
        // prospects. The projection and its gaps are derived and go without argument; the sitting
        // is a record that a named child sat a named paper on a named day, which is a fact about
        // them rather than about the paper.
        typeof(Domain.Assessment.ExamProjection),
        typeof(Domain.Assessment.AssessmentAdministration),

        // **The hardest one on this list, and it still goes.** A reported examination result is
        // the calibration sample's whole value and it is irreplaceable — and it is also the most
        // sensitive academic fact the platform holds about one child. Keeping it after an erasure
        // request because it is useful is exactly the reasoning erasure exists to overrule. The
        // aggregate a fitted calibration was built on survives in the fit; the row does not.
        typeof(Domain.Assessment.ReportedExamOutcome),

        // Who this child was to an organization, and to a guardian.
        //
        // **Cohort membership goes; the cohort does not.** "Was in 6B, autumn 2026" is a fact about
        // a named child, and a school does not get to keep it because it finds the roster useful.
        // The cohort itself is about the school, survives, and its historical aggregates were
        // computed at the time from evidence that is now gone — which is the correct outcome, not a
        // gap: a report that could still name them would not have honoured the erasure.
        //
        // **The org membership goes too**, including a teacher's. A revoked membership is kept for
        // audit while the person exists; an erased person leaves nothing to audit.
        typeof(Domain.Organizations.CohortMembership),
        typeof(Domain.Organizations.Membership),

        // Guardian links in **both directions** — one row keyed by the erased user as guardian, one
        // as learner. A link that survived would name a child who asked to be forgotten, from their
        // parent's account, which is the same disclosure by a different door. Handled by the purge's
        // own multi-column resolution rather than by two entries here.
        typeof(Domain.Organizations.GuardianLink),

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
        typeof(LeaderboardSettlement),

        // A prize claim before the award it belongs to, because the list is purged in order and the
        // claim's foreign key does not cascade. Both are deleted rather than anonymised, for the same
        // reason the standings are: "rank 3, a tablet, week 37" is re-identifiable to everybody who
        // was in that competition, and a child who asked to be forgotten should not leave a prize
        // record behind them. What they were actually paid stays in the currency ledger, which is the
        // economy's audit trail rather than a record about them.
        typeof(PrizeClaim),
        typeof(EventAward),

        // A content-team member's scope. These go anyway, by cascade from the StaffProfile, which
        // cascades from the account; listed because the guard reads direct foreign keys, and SQL
        // Server will not accept a second cascade path from AspNetUsers into the same table. In
        // practice nothing reaches here: staff accounts are deactivated, never deleted, and both
        // deletion paths refuse them.
        typeof(Domain.Staff.StaffScopeNode),
        typeof(Domain.Staff.StaffScopeLanguage)
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
    /// <summary>
    /// User-keyed tables that **outlive the account on purpose**: audit trails of what somebody did
    /// to the platform, as opposed to records about the person.
    /// <para>
    /// The rule is the currency ledger's, stated above for the standings: a record of an action on
    /// the system stays because it is the system's own accounting, while anything that describes the
    /// person goes. So these rows keep the actor's id — pseudonymous once the account is gone, since
    /// nothing is left to resolve it against — and <see cref="ScrubRetainedAsync"/> blanks every
    /// free-text personal detail they carry. A table only belongs here if its user column names the
    /// <i>actor</i>; a row about a learner is purged, whatever else it records.
    /// </para>
    /// <para>
    /// <c>AccountDeletionCoverageTests</c> accepts a type in this list as covered, so adding one is a
    /// deliberate, reviewed decision rather than a way to quiet the guard.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<Type> RetainedOnDeletion =
    [
        // The guidance CMS's change history. UserId is always the admin who acted — a user reset
        // names its target only inside DetailsJson, by id. The stored email is scrubbed.
        typeof(Domain.Guidance.GuidanceAuditLog)
    ];

    /// <summary>
    /// Blanks the personal details on the rows in <see cref="RetainedOnDeletion"/> that name this
    /// user, and reports how many rows were touched. Runs as part of <see cref="PurgeAsync"/>.
    /// </summary>
    public static Task<int> ScrubRetainedAsync(
        ApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        context.GuidanceAuditLogs
            .Where(l => l.UserId == userId && l.UserEmail != null)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.UserEmail, (string?)null), cancellationToken);

    public static async Task<int> PurgeAsync(
        ApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var deleted = 0;

        await ScrubRetainedAsync(context, userId, cancellationToken);

        foreach (var clrType in ManuallyPurged)
        {
            // Table and column names come from the EF model, never from input, so composing them
            // into SQL is safe. The user id stays a parameter.
            var entityType = context.Model.FindEntityType(clrType)
                ?? throw new InvalidOperationException($"{clrType.Name} is not part of the EF model.");

            var schema = entityType.GetSchema() ?? "dbo";
            var table = entityType.GetTableName()
                ?? throw new InvalidOperationException($"{clrType.Name} is not mapped to a table.");

            // A table may name the user more than once. A guardian link does: one column for the
            // guardian and one for the learner, and a link that survived because the erased person
            // was on the *other* end of it would name a child who asked to be forgotten, from their
            // parent's account. Every matching column is ORed rather than one being picked.
            var columns = UserKeyColumns(entityType);

            var predicate = string.Join(" OR ", columns.Select(c => $"[{c}] = {{0}}"));

            deleted += await context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM [{schema}].[{table}] WHERE {predicate}",
                [userId],
                cancellationToken);
        }

        return deleted;
    }

    /// <summary>
    /// Which column on a table names the account that owns the row.
    /// <para>
    /// Almost always <c>UserId</c>. The evidence log says <c>LearnerId</c> instead, because a
    /// learner is a role a user plays rather than the user themselves — teachers, guardians and
    /// school staff are all users, and none of them will ever own a response. Resolving the name
    /// from the model rather than assuming it keeps that distinction available without a table
    /// quietly escaping deletion, which is exactly what happened when this method assumed
    /// <c>UserId</c>.
    /// </para>
    /// </summary>
    public static string UserKeyColumn(IReadOnlyEntityType entityType) =>
        UserKeyColumns(entityType)[0];

    /// <summary>
    /// Every column on a table that names an account, in priority order.
    /// <para>
    /// Usually one. <c>GuardianLink</c> has two — a row belongs to the guardian and to the learner
    /// at once — and purging on only the first would leave the erased person named in the other's
    /// row.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> UserKeyColumns(IReadOnlyEntityType entityType)
    {
        var columns = new List<string>();

        foreach (var candidate in UserKeyProperties)
        {
            var property = entityType.FindProperty(candidate);
            if (property is not null) columns.Add(property.GetColumnName());
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException(
                $"{entityType.ClrType.Name} is listed for purge but carries none of: "
                + string.Join(", ", UserKeyProperties));
        }

        return columns;
    }

    /// <summary>
    /// The property names that mean "the account this row belongs to", in priority order. Shared
    /// with <c>AccountDeletionCoverageTests</c>, so a table naming its owner something new cannot
    /// slip past the guard by not being called <c>UserId</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> UserKeyProperties =
        ["UserId", "LearnerId", "GuardianUserId", "LearnerUserId"];
}
