using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Domain.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests.Contracts;

[Collection(ContractCollection.Name)]
public class TournamentContractTests(ContractHost host)
{
    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(host.ConnectionString).Options);

    [Fact]
    public async Task Teacher_routes_authorize_the_class_and_a_pairing_survives_an_abrupt_restart()
    {
        var teacher = host.Data.EnglishStudent; var learner = host.Data.ArabicStudent;
        await using var db = Context();
        var path = await TestData.CreateCurriculumPathAsync(db);
        var mode = await db.AddModeAsync(path.GameId, isDefault: true);
        mode.WinRuleJson = MatchWinRule.Build([((string?)MatchMetrics.CorrectAnswers, (string?)"higher")]).Rule!.ToJson();
        var other = await TestData.CreateUserAsync(db);
        var cohort = await SocialTest.ClassAsync(db, [learner.Id, other], teacher.Id);
        await db.SaveChangesAsync();
        using var teacherHttp = await host.SignedInAsync(teacher);
        using var learnerHttp = await host.SignedInAsync(learner);
        var created = await teacherHttp.PostAsJsonAsync("/api/multiplayer/tournaments", new
        { title = "Class cup", gameId = path.GameId, modeId = mode.Id, cohortId = cohort, format = "Swiss", maxEntrants = 2 });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var json = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        var route = $"/api/multiplayer/tournaments/{id}";
        Assert.Equal("Registration", json.RootElement.GetProperty("state").GetString());
        Assert.True(json.RootElement.GetProperty("canManage").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Http.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await learnerHttp.PostAsJsonAsync(route + "/start", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await learnerHttp.GetAsync("/api/admin/multiplayer/tournaments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await learnerHttp.PostAsJsonAsync(route + "/register", new { })).StatusCode);
        Assert.True((await MultiplayerTest.Tournaments(db).RegisterAsync(other, id)).Succeeded);
        var started = await teacherHttp.PostAsJsonAsync(route + "/start", new { });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var learnerView = await JsonDocument.ParseAsync(await (await learnerHttp.GetAsync(route)).Content.ReadAsStreamAsync());
        var match = learnerView.RootElement.GetProperty("myMatch").GetProperty("id").GetGuid();
        var playRoute = route + $"/matches/{match}/play";
        var retryKey = Guid.NewGuid().ToString();
        var play = new { protocolVersion = 1, transportSessionName = MultiplayerTest.NewTransportName(), requestId = retryKey };
        using var roomBefore = await JsonDocument.ParseAsync(await (await learnerHttp.PostAsJsonAsync(playRoute, play)).Content.ReadAsStreamAsync());
        var session = roomBefore.RootElement.GetProperty("session").GetProperty("id").GetGuid();
        Assert.True(roomBefore.RootElement.GetProperty("youHost").GetBoolean());
        await host.RestartAsync();
        using var roomAfter = await JsonDocument.ParseAsync(await (await learnerHttp.PostAsJsonAsync(playRoute, play)).Content.ReadAsStreamAsync());
        Assert.Equal(session, roomAfter.RootElement.GetProperty("session").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await learnerHttp.GetAsync($"/api/multiplayer/sessions/{session}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacherHttp.PostAsJsonAsync(route + "/cancel", new { reason = "Class finished" })).StatusCode);
        await using var check = Context();
        Assert.Equal(MultiplayerSessionState.Closed, (await check.MultiplayerSessions.SingleAsync(s => s.Id == session)).State);
    }
}
