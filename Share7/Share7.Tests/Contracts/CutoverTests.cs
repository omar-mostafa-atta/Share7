using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Staff;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests.Contracts;

/// <summary>
/// Cutover (plan P6) over the wire: there is exactly one way to author content, and it goes through
/// review.
/// <para>
/// The gate is a claim about the whole running API rather than about any one class, so it is tested
/// where the claim lives — against the real process, through the doors somebody would actually try.
/// Three of them: the old authoring routes, the old sign-in, and the Studio.
/// </para>
/// <para>
/// What it deliberately does not assert is that the old routes are <i>gone</i>. They answer, and
/// what they answer is where the work went; a 404 would have been indistinguishable from a typo.
/// </para>
/// </summary>
[Collection(StudioApiCollection.Name)]
public class CutoverTests
{
    private const string Password = "Blue-Harbour-Lantern-7";

    private readonly StudioApiHost _host;

    public CutoverTests(StudioApiHost host) => _host = host;

    // ------------------------------------------------------------------ the old authoring routes

    public static TheoryData<string, string, string> ClosedWrites() => new()
    {
        { "POST", "/api/admin/grades/{grade}/terms", "Building the curriculum" },
        { "POST", "/api/admin/subjects/{subject}/chapters", "Building the curriculum" },
        { "DELETE", "/api/admin/lessons/{lesson}", "Building the curriculum" },
        { "POST", "/api/admin/lessons/{lesson}/questions/manual", "Writing a lesson" },
        { "POST", "/api/admin/lessons/{lesson}/recovery-questions/manual", "Writing second-chance questions" },
        { "PUT", "/api/admin/lessons/{lesson}/sheet", "Editing a lesson sheet" },
        { "DELETE", "/api/admin/lessons/{lesson}/sheet/1", "Deleting a row from a lesson sheet" },
        { "POST", "/api/admin/assessment/targets/promote", "Replacing a stand-in with a real skill" },
    };

    [Theory]
    [MemberData(nameof(ClosedWrites))]
    public async Task An_old_authoring_route_answers_gone_and_says_where_the_work_went(
        string method, string route, string was)
    {
        using var admin = await SignedInAdminAsync();

        var path = route
            .Replace("{grade}", _host.Data.GradeId.ToString())
            .Replace("{subject}", _host.Data.ScienceId.ToString())
            .Replace("{lesson}", _host.Data.SolidsLessonId.ToString());

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "DELETE")
            request.Content = JsonContent.Create(new { });

        var response = await admin.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Gone, $"{method} {path} answered {(int)response.StatusCode}: {text}");

        var said = JsonNode.Parse(text)!["errors"]!.AsArray()[0]!.GetValue<string>();
        Assert.Contains(was, said);
        Assert.Contains("Content Studio", said);
    }

    [Fact]
    public async Task The_reads_beside_them_still_answer_because_a_read_was_never_a_second_way_in()
    {
        using var admin = await SignedInAdminAsync();

        var lang = _host.Data.EnglishStudent.LangId;

        foreach (var path in new[]
                 {
                     $"/api/admin/lessons/{_host.Data.SolidsLessonId}/questions?langId={lang}",
                     $"/api/admin/lessons/{_host.Data.SolidsLessonId}/sheet",
                     "/api/admin/curriculum/health",
                 })
        {
            var response = await admin.GetAsync(path);
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"{path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    // ------------------------------------------------------------------ the old sign-in

    [Fact]
    public async Task A_content_team_account_is_refused_the_old_sign_in_and_signs_in_at_the_studio()
    {
        var username = await NewMemberAsync();

        var refused = await _host.Http.PostAsJsonAsync("/api/auth/login", new { username, password = Password });
        var text = await refused.Content.ReadAsStringAsync();

        Assert.True(refused.StatusCode == HttpStatusCode.Unauthorized, $"the old sign-in answered {(int)refused.StatusCode}: {text}");
        Assert.Contains("Content Studio", JsonNode.Parse(text)!["errors"]!.AsArray()[0]!.GetValue<string>());

        // The right password, and still no game token — a closed door rather than a wrong key.
        Assert.DoesNotContain("accessToken", text);

        var studio = await _host.Http.PostAsJsonAsync("/api/studio/auth/sign-in", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, studio.StatusCode);
        Assert.NotNull((await studio.Content.ReadFromJsonAsync<JsonObject>())!["accessToken"]);
    }

    [Fact]
    public async Task An_administrator_still_signs_in_to_their_own_console()
    {
        // Cutover closes the content team's door, not everybody's. An Admin Console nobody can reach
        // would take the operational tools and the measurement reads down with it.
        using var admin = await SignedInAdminAsync();

        var response = await admin.GetAsync("/api/admin/curriculum/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private async Task<HttpClient> SignedInAdminAsync()
    {
        var login = await _host.Http.PostAsJsonAsync(
            "/api/auth/login", new { username = "admin", password = ContractHostSecrets.AdminPassword });

        var body = await login.Content.ReadFromJsonAsync<JsonObject>();
        var token = body?["accessToken"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Could not sign in the admin: {login.StatusCode} {body}");

        var client = new HttpClient { BaseAddress = _host.Http.BaseAddress, Timeout = _host.Http.Timeout };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A content-team account, made and activated the way Team &amp; Access makes one.</summary>
    private async Task<string> NewMemberAsync()
    {
        await using var services = StaffTestHost.Build(_host.ConnectionString);

        string username;
        string secret;

        await using (var scope = services.Request(Guid.NewGuid(), Domain.Constants.Roles.SuperAdmin))
        {
            await IdentityTestHost.EnsureRolesAsync(scope.Get<ApplicationDbContext>());

            var created = await scope.Get<ITeamAdminService>().CreateMemberAsync(new CreateTeamMemberRequest(
                "Nour Hassan", $"c{Guid.NewGuid():N}"[..18], null, "Mathematics specialist", StudioRole.Author,
                AllNodes: true, NodeIds: null, AllLanguages: true, LanguageIds: null, InterfaceLanguage: "en"));

            Assert.True(created.Succeeded, string.Join("; ", created.Errors));
            username = created.Value!.Member.Username;
            secret = created.Value.SetupLink.Url[(created.Value.SetupLink.Url.IndexOf('#') + 1)..];
        }

        await using (var scope = services.Request())
        {
            var activated = await scope.Get<IStudioAuthService>().CompleteSetupAsync(
                new StudioSetupCompleteRequest(secret, Password, "en"),
                new StudioClientInfo("198.51.100.23", "Share7Tests/1.0"));

            Assert.True(activated.Succeeded, activated.Error?.Code);
        }

        return username;
    }
}
