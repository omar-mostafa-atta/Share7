using Share7.Application.Games.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Games;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// The authoring read behind the admin console's Edit button.
/// <para>
/// It exists for one reason: <c>UpdateAsync</c> is a full replace, and the client-facing read
/// resolves a single translation from the caller's content language. An edit form filled from that
/// read would send one language back on its own and silently delete every other one — so the
/// property under test is that this read returns <b>all</b> of them.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class GameAuthoringReadTests
{
    private readonly SqlServerFixture _fixture;

    public GameAuthoringReadTests(SqlServerFixture fixture) => _fixture = fixture;

    private static GameAdminService Admin(ApplicationDbContext context) =>
        new(context, new GameService(context, new StubLanguageService(LanguageIds.English)));

    private static SaveGameRequest Request(string key) => new()
    {
        GameKey = key,
        LobbyScene = 1,
        GameplayScene = 2,
        MinPlayers = 1,
        MaxPlayers = 4,
        ReadyTimeoutSeconds = 30f,
        Translations =
        [
            new GameTranslationRequest { LangId = LanguageIds.English, DisplayName = "Runner", Description = "Run." },
            new GameTranslationRequest { LangId = LanguageIds.Arabic, DisplayName = "عداء", Description = "اركض." }
        ]
    };

    [Fact]
    public async Task It_returns_every_language_not_just_the_callers()
    {
        var request = Request($"authoring_{Guid.NewGuid():N}"[..24]);

        await using var context = _fixture.CreateContext();
        var created = await Admin(context).CreateAsync(request);
        Assert.True(created.Succeeded);

        var authoring = await Admin(context).GetForAuthoringAsync(created.Value!.GameId);

        Assert.NotNull(authoring);
        Assert.Equal(2, authoring!.Translations.Count);
        Assert.Contains(authoring.Translations, t => t.LangId == LanguageIds.English && t.DisplayName == "Runner");
        Assert.Contains(authoring.Translations, t => t.LangId == LanguageIds.Arabic && t.DisplayName == "عداء");
    }

    [Fact]
    public async Task What_it_reads_back_is_what_a_save_has_to_send()
    {
        // The round trip the Edit button performs: fill the form from this read, save it unchanged,
        // and nothing may move. A field missing from this DTO would be cleared by that save.
        var request = Request($"roundtrip_{Guid.NewGuid():N}"[..24]);
        request.GameplaySceneAddress = "Assets/Games/Runner/Scenes/GameRunner.unity";
        request.LobbySceneAddress = "Assets/Games/Runner/Scenes/RunnerLobby.unity";
        request.UseMatchmaking = false;

        await using var context = _fixture.CreateContext();
        var created = await Admin(context).CreateAsync(request);
        Assert.True(created.Succeeded);

        var authoring = await Admin(context).GetForAuthoringAsync(created.Value!.GameId);
        Assert.NotNull(authoring);

        var resave = new SaveGameRequest
        {
            GameKey = authoring!.GameKey,
            LobbyScene = authoring.LobbyScene,
            GameplayScene = authoring.GameplayScene,
            LobbySceneAddress = authoring.LobbySceneAddress,
            GameplaySceneAddress = authoring.GameplaySceneAddress,
            MinPlayers = authoring.MinPlayers,
            MaxPlayers = authoring.MaxPlayers,
            ReadyTimeoutSeconds = authoring.ReadyTimeoutSeconds,
            SupportsSinglePlayer = authoring.SupportsSinglePlayer,
            SupportsMultiplayer = authoring.SupportsMultiplayer,
            UseLobby = authoring.UseLobby,
            UseMatchmaking = authoring.UseMatchmaking,
            IsActive = authoring.IsActive,
            Translations = [.. authoring.Translations]
        };

        var updated = await Admin(context).UpdateAsync(authoring.GameId, resave);
        Assert.True(updated.Succeeded);

        var after = await Admin(context).GetForAuthoringAsync(authoring.GameId);

        Assert.NotNull(after);
        Assert.Equal(2, after!.Translations.Count);
        Assert.Equal(authoring.GameKey, after.GameKey);
        Assert.Equal(authoring.GameplaySceneAddress, after.GameplaySceneAddress);
        Assert.Equal(authoring.LobbySceneAddress, after.LobbySceneAddress);
        Assert.Equal(authoring.MaxPlayers, after.MaxPlayers);
        Assert.Equal(authoring.ReadyTimeoutSeconds, after.ReadyTimeoutSeconds);
        Assert.False(after.UseMatchmaking);
    }

    [Fact]
    public async Task An_id_that_is_not_a_game_reads_back_null()
    {
        await using var context = _fixture.CreateContext();

        Assert.Null(await Admin(context).GetForAuthoringAsync(Guid.NewGuid()));
    }
}
