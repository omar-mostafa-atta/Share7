using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Share7.Application.Staff.Interfaces;
using Share7.Application.Staff.Models;
using Share7.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests.Contracts;

/// <summary>
/// Scope limits what a member may change, everywhere — including the acts that happen at once
/// rather than through a draft.
/// <para>
/// The draft machinery has enforced this from Phase 3: a draft is opened, reviewed and released
/// against the member's part of the curriculum. Phase 5 added five acts that bypass drafts on
/// purpose — saying what a question measures, replacing a stand-in, marking an anchor, stopping
/// answers counting, and building a paper — and every one of them was gated on the Studio role
/// alone. A Lead of Primary 1 Mathematics could withdraw the answers to any question on the
/// platform. These pin the rule for them.
/// </para>
/// <para>
/// Read is deliberately not scoped, and one test says so: everyone in the Studio sees all of the
/// curriculum, which is the other half of the same rule.
/// </para>
/// </summary>
[Collection(StudioApiCollection.Name)]
public class StudioScopeTests
{
    private const string Password = "Blue-Harbour-Lantern-7";

    private readonly StudioApiHost _host;

    public StudioScopeTests(StudioApiHost host) => _host = host;

    [Fact]
    public async Task A_member_cannot_say_what_a_question_outside_their_scope_measures()
    {
        using var lead = await ScopedLeadAsync();

        var outside = await AnItemInAsync(lead, _host.Data.SolidsLessonId);
        var refused = await Put(lead, $"/api/studio/skills/questions/{outside}", new
        {
            skills = new[] { new { targetId = Guid.NewGuid(), emphasis = 1.0m, isPrimary = true } }
        });

        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Equal("OUT_OF_SCOPE", refused.Body!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_member_cannot_anchor_or_withdraw_answers_outside_their_scope()
    {
        using var lead = await ScopedLeadAsync();

        var outside = await AnItemInAsync(lead, _host.Data.SolidsLessonId);

        var anchor = await Post(lead, $"/api/studio/quality/questions/{outside}/anchor", new
        {
            isAnchor = true,
            reason = "Trying to reach into somebody else's subject."
        });

        Assert.Equal(HttpStatusCode.Forbidden, anchor.Status);
        Assert.Equal("OUT_OF_SCOPE", anchor.Body!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_member_cannot_build_a_paper_over_a_subject_outside_their_scope()
    {
        using var lead = await ScopedLeadAsync();

        var refused = await Post(lead, "/api/studio/exams/benchmark", new
        {
            subjectNodeId = _host.Data.ScienceId,
            versionLabel = (string?)null,
            langId = _host.Data.EnglishStudent.LangId
        });

        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Equal("OUT_OF_SCOPE", refused.Body!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_same_member_reads_the_whole_curriculum_including_what_they_cannot_change()
    {
        using var lead = await ScopedLeadAsync();

        var outside = await AnItemInAsync(lead, _host.Data.SolidsLessonId);

        var read = await lead.GetAsync($"/api/studio/skills/questions/{outside}?langId={_host.Data.EnglishStudent.LangId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var tree = await lead.GetAsync("/api/studio/curriculum/nodes");
        Assert.Equal(HttpStatusCode.OK, tree.StatusCode);
    }

    [Fact]
    public async Task A_recovery_rule_can_be_written_at_a_grade()
    {
        // "Set at grade, subject or lesson" is how the feature is described, and the Recovery board
        // offers the first of them — it shows the grade, what it is inheriting, and a propose
        // action. Opening the draft used to answer DRAFT_INVALID / nodeKind, because the check
        // borrowed the predicate for *structural* edits, which the fourteen fixed grades fail.
        using var lead = await SignedInMemberAsync(StudioRole.Lead, allNodes: true);

        var draft = await Post(lead, "/api/studio/drafts", new { kind = "RecoveryRule", nodeId = _host.Data.GradeId });

        Assert.True(draft.Status == HttpStatusCode.OK, $"a grade-level rule was refused: {draft.Body}");
        Assert.Equal("RecoveryRule", draft.Body!["summary"]!["kind"]!.GetValue<string>());
    }

    // ---- helpers ---------------------------------------------------------------------------

    private sealed record Answer(HttpStatusCode Status, JsonObject? Body);

    /// <summary>The id of a question published in a lesson, read the way the Studio reads it.</summary>
    private async Task<Guid> AnItemInAsync(HttpClient client, Guid lessonId)
    {
        var response = await client.GetAsync($"/api/studio/curriculum/lessons/{lessonId}/workspace");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"could not read lesson {lessonId}: {body}");

        return body!["live"]!["items"]!.AsArray()[0]!["itemId"]!.GetValue<Guid>();
    }

    /// <summary>
    /// A Lead whose remit is the fixture's <i>other</i> chapter — Energy — so every lesson the
    /// tests act on is outside it.
    /// <para>
    /// A scope is set at a grade, term, subject or chapter and never at a lesson: "lessons are too
    /// fine to hand someone as a remit" (<c>StaffScopeReader.ScopeKinds</c>). That is why this is
    /// not simply "scoped to one lesson".
    /// </para>
    /// </summary>
    private async Task<HttpClient> ScopedLeadAsync()
    {
        await using var services = StaffTestHost.Build(_host.ConnectionString);
        await using var scope = services.Request();

        var elsewhere = await scope.Get<ApplicationDbContext>().CurriculumNodes
            .AsNoTracking()
            .Where(n => n.ParentNodeId == _host.Data.ScienceId && n.Id != _host.Data.MatterChapterId)
            .Select(n => n.Id)
            .FirstAsync();

        return await SignedInMemberAsync(StudioRole.Lead, allNodes: false, nodeIds: [elsewhere]);
    }

    private async Task<HttpClient> SignedInMemberAsync(StudioRole role, bool allNodes, IReadOnlyList<Guid>? nodeIds = null)
    {
        await using var services = StaffTestHost.Build(_host.ConnectionString);

        string username;
        string secret;

        await using (var scope = services.Request(Guid.NewGuid(), Domain.Constants.Roles.SuperAdmin))
        {
            await IdentityTestHost.EnsureRolesAsync(scope.Get<ApplicationDbContext>());

            var created = await scope.Get<ITeamAdminService>().CreateMemberAsync(new CreateTeamMemberRequest(
                "Salma Farid", $"s{Guid.NewGuid():N}"[..18], null, "Physics specialist", role,
                AllNodes: allNodes, NodeIds: nodeIds, AllLanguages: true, LanguageIds: null, InterfaceLanguage: "en"));

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

        var response = await _host.Http.PostAsJsonAsync("/api/studio/auth/sign-in", new { username, password = Password });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();

        var token = body?["accessToken"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Studio sign-in failed: {response.StatusCode} {body}");

        var client = new HttpClient { BaseAddress = _host.Http.BaseAddress, Timeout = _host.Http.Timeout };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
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
