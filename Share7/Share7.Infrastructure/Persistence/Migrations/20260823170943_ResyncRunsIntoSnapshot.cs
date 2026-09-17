using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// **Deliberately does nothing. It exists for its side effect on the model snapshot.**
    /// <para>
    /// Two sessions built features into this solution in parallel, and each ran <c>migrations add</c>
    /// against its own copy of the model. <c>ApplicationDbContextModelSnapshot.cs</c> is a single
    /// shared file, so every generation overwrote the other's: the Designer snapshots alternate
    /// between one that knows about <c>Runs</c>/<c>PickupValuations</c>/<c>RunPayouts</c>/
    /// <c>DailyCurrencyLedger</c> and one that knows about the leaderboard and progression tables. The
    /// last write won, and it had never heard of the runs feature.
    /// </para>
    /// <para>
    /// A stale snapshot is not cosmetic — it is the baseline every future <c>migrations add</c> diffs
    /// against. Left alone, the next migration anyone generated would have scaffolded
    /// <c>CreateTable</c> for four tables that already exist, or <c>DropTable</c> for four that are
    /// still in use, depending on which side generated it. Scaffolding this migration is what forced
    /// EF to write a snapshot describing the **whole** model, both workstreams included.
    /// </para>
    /// <para>
    /// The scaffolded body is discarded rather than kept, because every object in it was already
    /// created earlier in the chain — <c>RunsAndPickupValuations</c> made the four tables and
    /// <c>RunBoundsReviewAndHardCurrency</c> added <c>Currencies.DailyEarnCap</c>. Running it would
    /// fail on a fresh database with "There is already an object named 'Runs'", and reverting it would
    /// drop live tables. An empty migration applies cleanly to both a fresh database and one already
    /// carrying the earlier migrations, which is the only shape that is safe here.
    /// </para>
    /// </summary>
    public partial class ResyncRunsIntoSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see the class summary. The schema this migration's snapshot
            // describes was created by RunsAndPickupValuations and RunBoundsReviewAndHardCurrency.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. The scaffolded Down dropped the four run tables and DailyEarnCap,
            // which this migration never created and must not take away.
        }
    }
}
