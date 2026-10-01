using Microsoft.EntityFrameworkCore;
using Share7.Application.Common.Models;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Sets a session's <c>CurrentPlayerCount</c> to the seats actually held, **in one statement**.
/// <para>
/// Shared by every path that frees a seat without the capacity UPDATE's own arithmetic — the sweeper
/// releasing a missing player, the host removing one — so the rule cannot drift between them. It
/// used to be two statements, count then write, and a join committing between them was silently
/// undone: the count went back to what it was before the join while the joiner kept their seat, and
/// the session then admitted one player more than it had room for. Counting inside the UPDATE means
/// the number written is the number of seats at the moment it is written.
/// </para>
/// </summary>
internal static class SeatCounts
{
    public static Task<int> RecountAsync(
        ApplicationDbContext dbContext, Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE [MultiplayerSessions]
            SET [CurrentPlayerCount] = (
                SELECT COUNT(*)
                FROM [MultiplayerSessionPlayers] AS [p]
                WHERE [p].[SessionId] = [MultiplayerSessions].[Id]
                  AND [p].[Status] <> {1}
                  AND [p].[Status] <> {2})
            WHERE [Id] = {0}
            """,
            [
                sessionId,
                WireEnum.ToWire(SessionPlayerStatus.Left),
                WireEnum.ToWire(SessionPlayerStatus.Removed)
            ],
            cancellationToken);
}
