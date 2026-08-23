using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives the runner's coin a price, so a settled run actually pays on deploy.
    /// <para>
    /// Without a valuation row every pickup resolves to "pays nothing" — correct behaviour for an
    /// unpriced kind, and a silent zero-payout economy on the day the client ships. There is no admin
    /// API for valuations until phase 3, so seeding here is what stops the gate depending on somebody
    /// remembering to run an <c>INSERT</c> by hand.
    /// </para>
    /// <para>
    /// **One coin per coin, and that is a starting value, not a decision.** It matches what the
    /// prefabs did before BC-COM-04, so the economy does not silently change under existing players
    /// on the day authority moves to the server. Retuning it is an <c>UPDATE</c> — no deploy, no
    /// client release. That is the entire point of the table.
    /// </para>
    /// <para>
    /// <c>MaxPerRun = 500</c> is a bound, not a balance knob: generous enough that no legitimate run
    /// is clipped, tight enough that a forged claim of 10,000 cannot pay. It is the *only* ceiling
    /// until the account-wide daily one lands in phase 2, which is why the column is mandatory.
    /// </para>
    /// </summary>
    public partial class SeedDefaultCoinValuation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Resolved by currency *key* rather than a hard-coded id: 'coins' has a different row id
            // in every environment, and a literal Guid here would seed production and silently seed
            // nothing anywhere else. Guarded by NOT EXISTS so re-running against a database that
            // already has the row is a no-op rather than a unique violation.
            migrationBuilder.Sql(
                """
                INSERT INTO [PickupValuations]
                    ([Id], [GameId], [PickupKind], [CurrencyId], [UnitValue], [MaxPerRun], [MaxPerDay], [Enabled], [CreatedAtUtc], [UpdatedAtUtc])
                SELECT NEWID(), NULL, 'coin', c.[Id], 1, 500, NULL, 1, SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM [Currencies] c
                WHERE c.[Key] = 'coins'
                  AND NOT EXISTS (
                      SELECT 1 FROM [PickupValuations] v
                      WHERE v.[GameId] IS NULL AND v.[PickupKind] = 'coin' AND v.[CurrencyId] = c.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the untouched seed row goes. A price somebody has since retuned is theirs, and
            // reverting a migration is not consent to lose an economy decision — the RunPayout rows
            // referencing it have to stay explicable either way.
            migrationBuilder.Sql(
                """
                DELETE v
                FROM [PickupValuations] v
                INNER JOIN [Currencies] c ON c.[Id] = v.[CurrencyId]
                WHERE v.[GameId] IS NULL
                  AND v.[PickupKind] = 'coin'
                  AND c.[Key] = 'coins'
                  AND v.[UnitValue] = 1
                  AND v.[MaxPerRun] = 500;
                """);
        }
    }
}
