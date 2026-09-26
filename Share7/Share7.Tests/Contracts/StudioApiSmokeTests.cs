using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Staff;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests.Contracts;

/// <summary>
/// The Studio's workspace API over the wire, against the real API process: the routes exist, they
/// accept only Studio sign-ins, the bodies bind, and the JSON is the shape the Studio app will read
/// (enums by name, the envelope on a refusal).
/// <para>
/// The member is made the way a SuperAdmin makes one — created in Team &amp; Access, activated
/// through their setup link — and then signs in over HTTP like anybody else.
/// </para>
/// </summary>
[Collection(StudioApiCollection.Name)]
public class StudioApiSmokeTests
{
    private const string Password = "Blue-Harbour-Lantern-7";

    private readonly StudioApiHost _host;

    public StudioApiSmokeTests(StudioApiHost host) => _host = host;

    [Fact]
    public async Task A_member_signs_in_and_works_a_lesson_draft_through_the_api()
    {
        using var studio = await SignedInMemberAsync(StudioRole.Lead);

        // ── the curriculum, as the Studio browses it ────────────────────────────────────────
        var languages = await Json(studio, "/api/studio/curriculum/languages");
        Assert.Contains(languages!.AsArray(), l => l!["code"]!.GetValue<string>() == "ar" && l["direction"]!.GetValue<string>() == "rtl");

        var grades = await Json(studio, "/api/studio/curriculum/nodes");
        Assert.All(grades!.AsArray(), g => Assert.Equal("grade", g!["kind"]!.GetValue<string>()));

        var workspace = await Json(studio, $"/api/studio/curriculum/lessons/{_host.Data.SolidsLessonId}/workspace");
        Assert.Equal(3, workspace!["live"]!["items"]!.AsArray().Count(i => i!["role"]!.GetValue<string>() == "Core"));
        Assert.Null(workspace["openDraft"]);

        // ── a draft, autosaved ─────────────────────────────────────────────────────────────
        var draft = await Post(studio, "/api/studio/drafts", new { kind = "LessonContent", nodeId = _host.Data.SolidsLessonId });
        Assert.Equal(HttpStatusCode.OK, draft.Status);

        var draftId = draft.Body!["summary"]!["id"]!.GetValue<Guid>();
        var revision = draft.Body["summary"]!["revision"]!.GetValue<int>();
        var proposal = draft.Body["proposal"]!.DeepClone();

        // Opening it again joins the same draft rather than starting a second.
        var again = await Post(studio, "/api/studio/drafts", new { kind = "LessonContent", nodeId = _host.Data.SolidsLessonId });
        Assert.Equal(draftId, again.Body!["summary"]!["id"]!.GetValue<Guid>());

        var saved = await Put(studio, $"/api/studio/drafts/{draftId}", new { revision, proposal });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal(revision + 1, saved.Body!["summary"]!["revision"]!.GetValue<int>());

        // The same save again is a conflict, with the revision to reload.
        var stale = await Put(studio, $"/api/studio/drafts/{draftId}", new { revision, proposal });
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal("DRAFT_REVISION_MOVED", stale.Body!["code"]!.GetValue<string>());
        Assert.Equal(revision + 1, stale.Body["details"]!["revision"]!.GetValue<int>());

        // ── the checks, the queue, the sheet ───────────────────────────────────────────────
        var problems = await Post(studio, $"/api/studio/drafts/{draftId}/check", new { });
        Assert.Equal(HttpStatusCode.OK, problems.Status);

        var queue = await Json(studio, "/api/studio/reviews/queue");
        Assert.Empty(queue!.AsArray());

        var template = await studio.GetAsync("/api/studio/imports/template?languages=en,ar");
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", template.Content.Headers.ContentType?.MediaType);

        // ── nothing the game reads has moved ───────────────────────────────────────────────
        using var student = await _host.SignedInAsync(_host.Data.EnglishStudent);
        var questions = await student.GetFromJsonAsync<JsonObject>($"/api/lessons/{_host.Data.SolidsLessonId}/questions");
        Assert.Equal(2, questions!["version"]!.GetValue<int>());
        Assert.Equal(3, questions["questions"]!.AsArray().Count);
    }

    [Fact]
    public async Task The_workspace_is_for_studio_sign_ins_only()
    {
        // A student's token is a game token; the Studio routes do not even read it.
        using var student = await _host.SignedInAsync(_host.Data.EnglishStudent);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.GetAsync("/api/studio/reviews/queue")).StatusCode);

        // And no token at all is the same answer.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.Http.GetAsync("/api/studio/curriculum/nodes")).StatusCode);
    }

    [Fact]
    public async Task An_author_cannot_build_a_release_and_is_told_why()
    {
        using var studio = await SignedInMemberAsync(StudioRole.Author);

        var refused = await Post(studio, "/api/studio/releases", new { title = "Mine", draftIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Equal("OUT_OF_SCOPE", refused.Body!["code"]!.GetValue<string>());
        Assert.Equal("role", refused.Body["details"]!["reason"]!.GetValue<string>());
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>A member made in Team &amp; Access, activated through their link, signed in over HTTP.</summary>
    private async Task<HttpClient> SignedInMemberAsync(StudioRole role)
    {
        await using var services = StaffTestHost.Build(_host.ConnectionString);

        string username;
        string secret;

        await using (var scope = services.Request(Guid.NewGuid(), Domain.Constants.Roles.SuperAdmin))
        {
            await IdentityTestHost.EnsureRolesAsync(scope.Get<ApplicationDbContext>());

            var created = await scope.Get<ITeamAdminService>().CreateMemberAsync(new CreateTeamMemberRequest(
                "Mona Adel", $"m{Guid.NewGuid():N}"[..18], null, "Science specialist", role,
                AllNodes: true, NodeIds: null, AllLanguages: true, LanguageIds: null, InterfaceLanguage: "en"));

            Assert.True(created.Succeeded, string.Join("; ", created.Errors));
            username = created.Value!.Member.Username;
            secret = created.Value.SetupLink!.Url[(created.Value.SetupLink.Url.IndexOf('#') + 1)..];
        }

        await using (var scope = services.Request())
        {
            var activated = await scope.Get<IStudioAuthService>().CompleteSetupAsync(
                new StudioSetupCompleteRequest(secret, Password, "en"),
                new StudioClientInfo("198.51.100.23", "Share7Tests/1.0"));

            Assert.True(activated.Succeeded, activated.Error?.Code);
        }

        var response = await _host.Http.PostAsJsonAsync("/api/studio/auth/sign-in", new { username, password = Password });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        var token = body?["accessToken"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Studio sign-in failed: {response.StatusCode} {body}");

        var client = new HttpClient { BaseAddress = _host.Http.BaseAddress, Timeout = _host.Http.Timeout };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record Answer(HttpStatusCode Status, JsonObject? Body);

    private static async Task<JsonNode?> Json(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonNode>();
    }

    private static async Task<Answer> Post(HttpClient client, string path, object body) =>
        await Read(await client.PostAsJsonAsync(path, body));

    private static async Task<Answer> Put(HttpClient client, string path, object body) =>
        await Read(await client.PutAsJsonAsync(path, body));

    private static async Task<Answer> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text) as JsonObject;
        return new Answer(response.StatusCode, body);
    }
}

/// <summary>The API process for the Studio smoke tests — its own database, and its own fixture data.</summary>
public sealed class StudioApiHost : ContractHost
{
}

[CollectionDefinition(Name)]
public class StudioApiCollection : ICollectionFixture<StudioApiHost>
{
    public const string Name = "studio-api";
}
