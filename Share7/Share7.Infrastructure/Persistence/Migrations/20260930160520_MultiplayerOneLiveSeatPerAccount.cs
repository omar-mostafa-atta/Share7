using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Share7.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// One account, one live seat, across every session — as an index rather than a service check.
    /// <para>
    /// **The repair runs first because production can already hold what the index forbids.** The
    /// cross-session race this closes was live, so an account may be seated in two sessions today, and
    /// a crash between a session's terminal write and its seat release could have stranded a seat in an
    /// ended session. Either would make the index creation fail, and a migration failing at startup
    /// takes the whole API down with it. Both repairs only ever release seats nobody can be using:
    /// </para>
    /// <list type="number">
    /// <item>Seats in sessions that already ended — released, exactly as the sweeper's healing rule
    /// does from now on.</item>
    /// <item>An account seated in several live sessions keeps its most recent seat, which is the match
    /// its client is actually in; the older one is the ghost.</item>
    /// <item>Every live session's count is recomputed from its seats, because step 2 changed some.</item>
    /// </list>
    /// <para>
    /// The statements are constants on this class, frozen with the migration, so
    /// <c>MultiplayerSeatRepairTests</c> can run them against deliberately violating rows. A repair
    /// that has only ever run against an empty table has not been tested.
    /// </para>
    /// </summary>
    public partial class MultiplayerOneLiveSeatPerAccount : Migration
    {
        public const string ReleaseSeatsInEndedSessionsSql =
            """
            UPDATE [p]
            SET [p].[Status] = 'LEFT',
                [p].[LeftAtUtc] = COALESCE([s].[EndedAtUtc], SYSUTCDATETIME())
            FROM [MultiplayerSessionPlayers] AS [p]
            INNER JOIN [MultiplayerSessions] AS [s] ON [s].[Id] = [p].[SessionId]
            WHERE [p].[Status] <> 'LEFT' AND [p].[Status] <> 'REMOVED'
              AND [s].[State] IN ('CLOSED', 'ABANDONED', 'FAILED');
            """;

        public const string KeepOnlyNewestLiveSeatSql =
            """
            WITH [ranked] AS (
                SELECT [p].[Id],
                       ROW_NUMBER() OVER (PARTITION BY [p].[UserId]
                                          ORDER BY [p].[JoinedAtUtc] DESC, [p].[Id]) AS [rn]
                FROM [MultiplayerSessionPlayers] AS [p]
                WHERE [p].[Status] <> 'LEFT' AND [p].[Status] <> 'REMOVED')
            UPDATE [p]
            SET [p].[Status] = 'LEFT',
                [p].[LeftAtUtc] = SYSUTCDATETIME(),
                [p].[IsHost] = 0
            FROM [MultiplayerSessionPlayers] AS [p]
            INNER JOIN [ranked] AS [r] ON [r].[Id] = [p].[Id]
            WHERE [r].[rn] > 1;
            """;

        public const string RecountLiveSessionsSql =
            """
            UPDATE [s]
            SET [s].[CurrentPlayerCount] = (
                SELECT COUNT(*)
                FROM [MultiplayerSessionPlayers] AS [p]
                WHERE [p].[SessionId] = [s].[Id]
                  AND [p].[Status] <> 'LEFT' AND [p].[Status] <> 'REMOVED')
            FROM [MultiplayerSessions] AS [s]
            WHERE [s].[State] NOT IN ('CLOSED', 'ABANDONED', 'FAILED');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ReleaseSeatsInEndedSessionsSql);
            migrationBuilder.Sql(KeepOnlyNewestLiveSeatSql);
            migrationBuilder.Sql(RecountLiveSessionsSql);

            migrationBuilder.CreateIndex(
                name: "UQ_SessionPlayer_OneLiveSeat",
                table: "MultiplayerSessionPlayers",
                column: "UserId",
                unique: true,
                filter: "[Status] <> 'LEFT' AND [Status] <> 'REMOVED'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The repair is not reversed: it released only seats nobody could be using, and putting a
            // ghost back in somebody's lobby is not a state worth restoring.
            migrationBuilder.DropIndex(
                name: "UQ_SessionPlayer_OneLiveSeat",
                table: "MultiplayerSessionPlayers");
        }
    }
}
