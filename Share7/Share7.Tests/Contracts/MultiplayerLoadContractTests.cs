using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Domain.Constants;
using Share7.Infrastructure.Persistence;
using Share7.Multiplayer.Load;
using Share7.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Share7.Tests.Contracts;

[Collection(ContractCollection.Name)]
public class MultiplayerLoadContractTests(ContractHost host, ITestOutputHelper output)
{
    [Fact]
    public async Task The_load_harness_exercises_real_HTTP_routes_and_cleans_up_every_membership()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(host.ConnectionString).Options);
        var path = await TestData.CreateCurriculumPathAsync(db);
        await db.AddModeAsync(path.GameId, isDefault: true);
        var fixtureHash = await db.Users.Where(u => u.Id == host.Data.EnglishStudent.Id).Select(u => u.PasswordHash).SingleAsync();
        var players = new List<LoadAccount>();
        for (var i = 0; i < 4; i++)
        {
            var userId = await TestData.CreateUserAsync(db);
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, fixtureHash));
            var name = await db.Users.Where(u => u.Id == userId).Select(u => u.UserName).SingleAsync();
            using var signedIn = await host.SignedInAsync(new(userId, name!, LanguageIds.English));
            players.Add(new(userId, signedIn.DefaultRequestHeaders.Authorization!.Parameter!));
        }
        var report = await new LoadRunner(host.Http).RunAsync(new(path.GameId, 1, 3, 1, 1), players);
        output.WriteLine(JsonSerializer.Serialize(report));
        Assert.All(report.Routes, r => Assert.Equal(0, r.Failures));
        foreach (var route in new[] { "create", "confirm", "matchmaking", "heartbeat", "recover", "get", "leave", "close" })
            Assert.Contains(report.Routes, r => r.Route == route && r.Requests > 0);
        Assert.All(report.Routes, r => { Assert.True(r.P50Ms <= r.P95Ms); Assert.True(r.P95Ms <= r.P99Ms); });
        var ids = players.Select(p => p.UserId).ToArray();
        Assert.False(await db.MultiplayerSessionPlayers.AnyAsync(p => ids.Contains(p.UserId)
            && p.Status != Share7.Domain.Multiplayer.SessionPlayerStatus.Left && p.Status != Share7.Domain.Multiplayer.SessionPlayerStatus.Removed));
        Assert.DoesNotContain("AccessToken", JsonSerializer.Serialize(report));
    }
}
