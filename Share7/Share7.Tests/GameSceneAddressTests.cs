using Share7.Application.Common.Models;
using Share7.Application.Games.Models;
using Share7.Domain.Constants;
using Share7.Infrastructure.Games;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests;

/// <summary>
/// Scene addresses on the game catalogue — the field a downloadable mini-game is identified by.
/// <para>
/// A build index cannot name a scene that is not in the build, so a game whose scenes arrive on
/// demand has no index to give. These tests pin the three things the server is actually
/// responsible for: it serves the addresses unchanged, it refuses a half-authored pair, and it
/// keeps <c>null</c> distinct from <c>""</c> — because null is the flag a client reads as "this
/// game still uses the build indices".
/// </para>
/// <para>
/// The server never checks that an address <i>resolves</i>. It cannot see the client's content
/// catalogue. That check belongs to the Unity content validator.
/// </para>
/// </summary>
[Collection(SqlServerCollection.Name)]
public class GameSceneAddressTests
{
    private const string GameplayAddress = "Assets/Games/Runner/Scenes/GameRunner.unity";
    private const string LobbyAddress = "Assets/Games/Runner/Scenes/RunnerLobby.unity";

    private readonly SqlServerFixture _fixture;

    public GameSceneAddressTests(SqlServerFixture fixture) => _fixture = fixture;

    private static GameAdminService Admin(ApplicationDbContext context) =>
        new(context, new GameService(context, new StubLanguageService(LanguageIds.English)));

    /// <summary>
    /// A request with every language named, so validation fails for the reason under test rather
    /// than for a missing translation.
    /// </summary>
    private static SaveGameRequest Request(string key) => new()
    {
        GameKey = key,
        LobbyScene = 1,
        GameplayScene = 2,
        Translations =
        [
            new GameTranslationRequest { LangId = LanguageIds.English, DisplayName = "Runner" },
            new GameTranslationRequest { LangId = LanguageIds.Arabic, DisplayName = "عداء" }
        ]
    };

    [Fact]
    public async Task Addresses_are_served_back_unchanged()
    {
        var request = Request($"scene_addr_{Guid.NewGuid():N}");
        request.LobbySceneAddress = LobbyAddress;
        request.GameplaySceneAddress = GameplayAddress;

        await using var context = _fixture.CreateContext();
        var created = await Admin(context).CreateAsync(request);

        Assert.True(created.Succeeded);
        Assert.Equal(LobbyAddress, created.Value!.LobbySceneAddress);
        Assert.Equal(GameplayAddress, created.Value.GameplaySceneAddress);

        // The build indices survive alongside them. This is the whole migration story: a client
        // that has not moved yet keeps reading what it always read.
        Assert.Equal(1, created.Value.LobbyScene);
        Assert.Equal(2, created.Value.GameplayScene);
    }

    [Fact]
    public async Task Omitting_the_addresses_leaves_them_null_not_blank()
    {
        var request = Request($"scene_none_{Guid.NewGuid():N}");

        await using var context = _fixture.CreateContext();
        var created = await Admin(context).CreateAsync(request);

        Assert.True(created.Succeeded);

        // Null is the discriminator. An empty string here would read as an authored address that
        // resolves to nothing, and the failure would surface when a child launches the game.
        Assert.Null(created.Value!.LobbySceneAddress);
        Assert.Null(created.Value.GameplaySceneAddress);
    }

    [Fact]
    public async Task Whitespace_is_normalized_to_null()
    {
        var request = Request($"scene_blank_{Guid.NewGuid():N}");
        request.LobbySceneAddress = "   ";
        request.GameplaySceneAddress = "";

        await using var context = _fixture.CreateContext();
        var created = await Admin(context).CreateAsync(request);

        Assert.True(created.Succeeded);
        Assert.Null(created.Value!.LobbySceneAddress);
        Assert.Null(created.Value.GameplaySceneAddress);
    }

    [Fact]
    public async Task Surrounding_whitespace_is_trimmed()
    {
        var request = Request($"scene_trim_{Guid.NewGuid():N}");
        request.LobbySceneAddress = $"  {LobbyAddress}  ";
        request.GameplaySceneAddress = $"\t{GameplayAddress}\n";

        await using var context = _fixture.CreateContext();
        var created = await Admin(context).CreateAsync(request);

        Assert.True(created.Succeeded);
        Assert.Equal(LobbyAddress, created.Value!.LobbySceneAddress);
        Assert.Equal(GameplayAddress, created.Value.GameplaySceneAddress);
    }

    [Fact]
    public async Task A_lobby_address_without_a_gameplay_address_is_rejected()
    {
        var request = Request($"scene_lobby_only_{Guid.NewGuid():N}");
        request.LobbySceneAddress = LobbyAddress;

        await using var context = _fixture.CreateContext();
        var created = await Admin(context).CreateAsync(request);

        // Nothing would ever read it: the gameplay address is what puts a game on addressable
        // scenes at all.
        Assert.False(created.Succeeded);
        Assert.Equal(ServiceErrorKind.Validation, created.ErrorKind);
        Assert.Contains(created.Errors, e => e.Contains("gameplaySceneAddress is required"));
    }

    [Fact]
    public async Task A_lobby_game_on_addressable_scenes_must_address_its_lobby()
    {
        var request = Request($"scene_half_{Guid.NewGuid():N}");
        request.UseLobby = true;
        request.GameplaySceneAddress = GameplayAddress;

        await using var context = _fixture.CreateContext();
        var created = await Admin(context).CreateAsync(request);

        // The half-authored pair. Left to stand, it fails at session start on a child's device —
        // the worst place to find out.
        Assert.False(created.Succeeded);
        Assert.Equal(ServiceErrorKind.Validation, created.ErrorKind);
        Assert.Contains(created.Errors, e => e.Contains("lobbySceneAddress is required"));
    }

    [Fact]
    public async Task A_game_without_a_lobby_needs_only_the_gameplay_address()
    {
        var request = Request($"scene_no_lobby_{Guid.NewGuid():N}");
        request.UseLobby = false;
        request.GameplaySceneAddress = GameplayAddress;

        await using var context = _fixture.CreateContext();
        var created = await Admin(context).CreateAsync(request);

        Assert.True(created.Succeeded);
        Assert.Null(created.Value!.LobbySceneAddress);
        Assert.Equal(GameplayAddress, created.Value.GameplaySceneAddress);
    }

    [Fact]
    public async Task An_update_can_move_a_game_onto_addressable_scenes()
    {
        var key = $"scene_migrate_{Guid.NewGuid():N}";

        await using var creating = _fixture.CreateContext();
        var created = await Admin(creating).CreateAsync(Request(key));
        Assert.True(created.Succeeded);
        Assert.Null(created.Value!.GameplaySceneAddress);

        var update = Request(key);
        update.LobbySceneAddress = LobbyAddress;
        update.GameplaySceneAddress = GameplayAddress;

        await using var updating = _fixture.CreateContext();
        var updated = await Admin(updating).UpdateAsync(created.Value.GameId, update);

        Assert.True(updated.Succeeded);
        Assert.Equal(GameplayAddress, updated.Value!.GameplaySceneAddress);
    }

    [Fact]
    public async Task An_update_can_move_a_game_back_off_addressable_scenes()
    {
        var key = $"scene_revert_{Guid.NewGuid():N}";

        var request = Request(key);
        request.LobbySceneAddress = LobbyAddress;
        request.GameplaySceneAddress = GameplayAddress;

        await using var creating = _fixture.CreateContext();
        var created = await Admin(creating).CreateAsync(request);
        Assert.True(created.Succeeded);

        await using var updating = _fixture.CreateContext();
        var reverted = await Admin(updating).UpdateAsync(created.Value!.GameId, Request(key));

        // A full replace means omitting the addresses clears them — the rollback path if a
        // content build has to be pulled.
        Assert.True(reverted.Succeeded);
        Assert.Null(reverted.Value!.LobbySceneAddress);
        Assert.Null(reverted.Value.GameplaySceneAddress);
    }
}
