using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Share7.Domain.BrainPass;
using Share7.Domain.Leaderboards;
using Share7.Domain.Rewards;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests.Contracts;

[Collection(ContractCollection.Name)]
public sealed class SocialPlatformContractTests(ContractHost host)
{
    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(host.ConnectionString).Options);

    [Fact]
    public async Task Social_wire_enforces_roles_owns_privacy_and_filters_inbox_payloads()
    {
        var user = host.Data.EnglishStudent; var other = host.Data.ArabicStudent;
        using var client = await host.SignedInAsync(user); using var outsider = await host.SignedInAsync(other);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Http.GetAsync($"/api/social/profiles/{user.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/social/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/social/showcase/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/brain-pass")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/social/privacy", new
        { profile = "Nobody", presence = "Nobody", statistics = "Nobody", invitations = "Nobody", challenges = "Nobody", friendRequests = false })).StatusCode);
        using var self = await JsonDocument.ParseAsync(await (await client.GetAsync($"/api/social/profiles/{user.Id}")).Content.ReadAsStreamAsync());
        Assert.True(self.RootElement.GetProperty("isSelf").GetBoolean());
        Assert.False(self.RootElement.TryGetProperty("email", out _)); Assert.False(self.RootElement.TryGetProperty("age", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/social/profiles/{user.Id}")).StatusCode);
        await using var db = Context(); var eventId = Guid.NewGuid();
        db.PlayerEvents.Add(new() { EventId = eventId, RecipientUserId = user.Id, Type = "reward.granted", OccurredAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1), PayloadJson = JsonSerializer.Serialize(new { transactionId = Guid.NewGuid(), displayName = "Private Name", token = "Never deliver" }) });
        await db.SaveChangesAsync();
        using var inbox = await JsonDocument.ParseAsync(await (await client.GetAsync("/api/inbox")).Content.ReadAsStreamAsync());
        var item = inbox.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("eventId").GetGuid() == eventId);
        Assert.False(item.GetProperty("payload").TryGetProperty("displayName", out _)); Assert.False(item.GetProperty("payload").TryGetProperty("token", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PutAsJsonAsync($"/api/inbox/{eventId}/read", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/inbox/{eventId}/read", new { })).StatusCode);
        // Restore the fixture player's defaults for other contract scenarios in this collection.
        await client.PutAsJsonAsync("/api/social/privacy", new { profile = "Connections", presence = "Connections", statistics = "Friends", invitations = "Connections", challenges = "Connections", friendRequests = true });
    }

    [Fact]
    public async Task Brain_pass_claim_survives_restart_without_another_payout_or_notification()
    {
        var user = host.Data.EnglishStudent; using var client = await host.SignedInAsync(user);
        await using var db = Context(); var path = await TestData.CreateCurriculumPathAsync(db); var currency = await db.CreateCurrencyAsync();
        var rule = await db.CreateRewardRuleAsync(RewardEventType.BrainPassTier, [new(currency.Id, 25)]);
        var season = new BrainPassSeason { Id = Guid.NewGuid(), Key = Guid.NewGuid().ToString("N"), NameEn = "Season", NameAr = "موسم", StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(1), ClaimUntilUtc = DateTime.UtcNow.AddDays(2), State = BrainPassState.Published, Version = 1, CreatedAtUtc = DateTime.UtcNow };
        db.BrainPassSeasons.Add(season); db.BrainPassTiers.Add(new() { SeasonId = season.Id, Number = 1, Track = BrainPassTrack.Free, RequiredXp = 1, RewardRuleId = rule.Id });
        db.BrainPassXpRules.Add(new() { SeasonId = season.Id, Metric = "LESSONS_COMPLETED", MinimumValue = 1, UnitValue = 1, XpPerUnit = 1, MaxSourceXp = 1, DailyCap = 10 });
        db.GameResults.Add(new() { Id = Guid.NewGuid(), UserId = user.Id, GameId = path.GameId, Metric = "LESSONS_COMPLETED", Value = 1,
            SourceId = Guid.NewGuid(), SourceType = GameResultSource.Attempt, OccurredAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow, RequestId = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync();
        var route = $"/api/brain-pass/{season.Id}/tiers/1/Free/claim";
        var response = await client.PostAsJsonAsync(route, new { }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var first = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var transaction = first.RootElement.GetProperty("rewards")[0].GetProperty("transactionId").GetGuid();
        await host.RestartAsync();
        var retry = await client.PostAsJsonAsync(route, new { }); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var replay = await JsonDocument.ParseAsync(await retry.Content.ReadAsStreamAsync());
        Assert.True(replay.RootElement.GetProperty("replayed").GetBoolean());
        Assert.Equal(transaction, replay.RootElement.GetProperty("rewards")[0].GetProperty("transactionId").GetGuid());
        Assert.Equal(1, await db.RewardTransactions.CountAsync(r => r.Id == transaction));
        Assert.Equal(1, await db.PlayerEvents.CountAsync(e => e.RecipientUserId == user.Id && e.Type == "reward.granted" && e.PayloadJson.Contains(transaction.ToString())));
        await db.BrainPassSeasons.Where(s => s.Id == season.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, BrainPassState.Disabled));
    }
}
