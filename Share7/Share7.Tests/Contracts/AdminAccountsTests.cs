using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Share7.Tests.Contracts;

/// <summary>
/// Who creates which account, over the wire (decided 2026-09-26).
/// <para>
/// An Admin creates an account of every role but SuperAdmin — a content-team member included, with
/// their Studio role, scope and setup link. Creating is all an Admin does to the content team:
/// everything after the account exists (the team list, a member's record, suspending, resetting,
/// closing, sessions), the audit log and the staff security settings stay SuperAdmin-only. The line
/// is drawn at the door, so it is tested at the door, with a real Admin sign-in.
/// </para>
/// </summary>
[Collection(StudioApiCollection.Name)]
public class AdminAccountsTests
{
    private readonly StudioApiHost _host;

    public AdminAccountsTests(StudioApiHost host) => _host = host;

    [Fact]
    public async Task An_admin_is_offered_every_role_but_super_admin()
    {
        using var admin = await SignedInAdminAsync();

        var body = await admin.GetFromJsonAsync<JsonObject>("/api/admin/users/assignable-roles");
        var roles = body!["roles"]!.AsArray().Select(r => r!.GetValue<string>()).ToList();

        Assert.Equal(["Student", "ContentTeam", "Admin"], roles);
    }

    [Fact]
    public async Task An_admin_creates_another_admin_but_not_a_super_admin()
    {
        using var admin = await SignedInAdminAsync();

        var another = await admin.PostAsJsonAsync("/api/admin/users",
            new { username = Unique("adm"), password = "Harbour-Lantern-72", role = "Admin" });
        Assert.Equal(HttpStatusCode.Created, another.StatusCode);

        var above = await admin.PostAsJsonAsync("/api/admin/users",
            new { username = Unique("sup"), password = "Harbour-Lantern-72", role = "SuperAdmin" });
        Assert.Equal(HttpStatusCode.Forbidden, above.StatusCode);
    }

    [Fact]
    public async Task An_admin_adds_a_content_team_member_and_can_do_nothing_else_in_team_and_access()
    {
        using var admin = await SignedInAdminAsync();

        // What a scope is chosen from is theirs to read…
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/team/scope-options")).StatusCode);

        // …and the member is theirs to add, with the password they set: no link, no activation.
        var created = await admin.PostAsJsonAsync("/api/admin/team", new
        {
            fullName = "Nour Hassan",
            username = Unique("nour"),
            workEmail = (string?)null,
            jobTitle = "Mathematics specialist",
            studioRole = "Author",
            allNodes = true,
            nodeIds = (Guid[]?)null,
            allLanguages = true,
            languageIds = (Guid[]?)null,
            interfaceLanguage = "en",
            password = "Quiet-Orchard-Signal-42"
        });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var member = await created.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Null(member!["setupLink"]);
        Assert.Equal("Active", member["member"]!["status"]!.GetValue<string>());
        var userId = member["member"]!["userId"]!.GetValue<Guid>();

        // Everything after that is a SuperAdmin's.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/admin/team")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/admin/team/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/admin/team/{userId}/suspend", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/admin/team/{userId}/reset-access", new { clearTwoStep = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/admin/team/{userId}/password", new { password = "Another-Lantern-88", clearTwoStep = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/admin/team/security")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/admin/audit")).StatusCode);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

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
}
