using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Share7.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001180000_TournamentEventOwnership")]
public sealed class TournamentEventOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Do not silently cancel someone's bracket. An existing duplicate stops the upgrade for
        // operator reconciliation; no results, entries or prizes are rewritten by this migration.
        migrationBuilder.DropIndex("IX_Tournament_Event", "Tournaments");
        migrationBuilder.CreateIndex("UQ_Tournament_Event", "Tournaments", "EventId", unique: true,
            filter: "[EventId] IS NOT NULL AND [State] <> 'CANCELLED'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("UQ_Tournament_Event", "Tournaments");
        migrationBuilder.CreateIndex("IX_Tournament_Event", "Tournaments", "EventId");
    }
}
