using Microsoft.EntityFrameworkCore;
using Share7.Application.Multiplayer.Models;
using Share7.Domain.Multiplayer;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

[Collection(SqlServerCollection.Name)]
public class MultiplayerLoadTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task One_hundred_simultaneous_joiners_take_exactly_one_last_seat()
    {
        var session = await MultiplayerTest.OpenAsync(fixture);
        var players = new List<Guid>();
        await using (var setup = fixture.CreateContext())
            for (var i = 0; i < 100; i++) players.Add(await TestData.CreateUserAsync(setup));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joins = players.Select(async player =>
        {
            await ready.Task;
            await using var db = fixture.CreateContext();
            return await MultiplayerTest.Sessions(db).JoinAsync(player, session.SessionId,
                new JoinMultiplayerSessionRequest { ProtocolVersion = 1, RequestId = Guid.NewGuid().ToString() });
        }).ToArray();
        ready.SetResult();
        var replies = await Task.WhenAll(joins);
        Assert.Single(replies, r => r.Succeeded);
        Assert.All(replies.Where(r => !r.Succeeded), r => Assert.Equal("SESSION_FULL", r.Error?.Code));
        await using var check = fixture.CreateContext();
        Assert.Equal(2, (await MultiplayerTest.ReadSessionAsync(check, session.SessionId)).CurrentPlayerCount);
        Assert.Equal(2, await check.MultiplayerSessionPlayers.CountAsync(p => p.SessionId == session.SessionId
            && p.Status != SessionPlayerStatus.Left && p.Status != SessionPlayerStatus.Removed));
    }
}
