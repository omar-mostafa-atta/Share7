using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Share7.Tests.Contracts;

// ===========================================================================
// The frozen game contract, once per curriculum read model
//
// Each mode gets its own API process and its own database, written by the same fixture and held to
// the same reviewed baselines. A difference in any of them is a difference the Unity client would
// see — a stop, not a baseline to re-record.
// ===========================================================================

/// <summary>Curriculum:ReadModel = Legacy — the typed tables, as the game has always been served.</summary>
[Collection(ContractCollection.Name)]
public class GameContractTests : GameContractScenarios
{
    public GameContractTests(ContractHost host) : base(host) { }
}

/// <summary>Curriculum:ReadModel = Generic — the node tree and the item bank, the engine's source of truth.</summary>
[Collection(GenericContractCollection.Name)]
public class GameContractGenericTests : GameContractScenarios
{
    public GameContractGenericTests(GenericContractHost host) : base(host) { }
}

/// <summary>
/// Curriculum:ReadModel = Shadow — the typed tables serve, and every read is also answered from the
/// node tree and compared. After each scenario the shadow tally must show reads were compared and
/// none differed or failed; that tally is the production evidence the switch-over waits on.
/// </summary>
[Collection(ShadowContractCollection.Name)]
public class GameContractShadowTests : GameContractScenarios
{
    private readonly ShadowContractHost _host;

    public GameContractShadowTests(ShadowContractHost host) : base(host) => _host = host;

    protected override async Task AfterScenarioAsync()
    {
        using var admin = await _host.SignedInAdminAsync();
        var readModel = await admin.GetFromJsonAsync<JsonObject>("/api/admin/engine/read-model");

        Assert.Equal("Shadow", readModel!["readModel"]!.GetValue<string>());

        var checks = readModel["checks"]!.AsArray();
        Assert.NotEmpty(checks);

        var disagreements = checks
            .Where(c => c!["differed"]!.GetValue<long>() > 0 || c["failed"]!.GetValue<long>() > 0)
            .Select(c => $"{c!["read"]}: {c["lastDifferenceSample"]}")
            .ToList();

        Assert.True(disagreements.Count == 0, "The node reads disagreed with the typed reads:\n" + string.Join("\n", disagreements));
    }
}

public sealed class GenericContractHost : ContractHost
{
    protected override string ReadModel => "Generic";
}

public sealed class ShadowContractHost : ContractHost
{
    protected override string ReadModel => "Shadow";

    public async Task<HttpClient> SignedInAdminAsync()
    {
        var login = await Http.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = ContractHostSecrets.AdminPassword });
        var body = await login.Content.ReadFromJsonAsync<JsonObject>();

        var token = body?["accessToken"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Could not sign in the seed admin: {login.StatusCode} {body}");

        var client = new HttpClient { BaseAddress = Http.BaseAddress, Timeout = Http.Timeout };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

[CollectionDefinition(Name)]
public class GenericContractCollection : ICollectionFixture<GenericContractHost>
{
    public const string Name = "game-contract-generic";
}

[CollectionDefinition(Name)]
public class ShadowContractCollection : ICollectionFixture<ShadowContractHost>
{
    public const string Name = "game-contract-shadow";
}
