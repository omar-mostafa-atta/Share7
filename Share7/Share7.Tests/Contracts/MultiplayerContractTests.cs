using Microsoft.EntityFrameworkCore;
using Share7.Domain.Leaderboards;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests.Contracts;

/// <summary>
/// <b>The frozen multiplayer contract.</b> The session registry the Unity client drives around its
/// Photon room — create, confirm, join, heartbeat, start, host transfer, matchmake, leave, close, and
/// the refusals it maps onto <c>NetworkSessionErrorCode</c> — called the way the client calls it and
/// compared with a reviewed baseline.
/// <para>
/// Recorded before the platform work began, so every change to this surface since shows up here as a
/// reviewed diff rather than as a surprise on a child's phone. Exact-lesson paths only: that is what
/// the shipped build sends. Subject matchmaking postdates it and is covered by its own service tests.
/// </para>
/// <para>
/// **One deliberate change since that first recording (2026-09-30):** <c>displayName</c> on a seat is
/// the player's generated public handle, no longer <c>StudentProfile.FullName</c>. Same field, same
/// type, same nullability — the client renders whatever string arrives — but a public match seats
/// strangers together, and a child's real name is not something the platform shows a stranger. The
/// re-recorded diff touched that value and nothing else. See <c>MultiplayerOptions.RosterNames</c>.
/// </para>
/// <para>
/// Only public sessions appear in the baseline, because a private session's join code is random. The
/// code's presence is asserted separately rather than normalised away, so a private session that
/// stopped returning one would still fail.
/// </para>
/// </summary>
[Collection(ContractCollection.Name)]
public class MultiplayerContractTests
{
    private readonly ContractHost _host;
    private ContractData Data => _host.Data;

    public MultiplayerContractTests(ContractHost host) => _host = host;

    [Fact]
    public async Task Playing_a_match_together()
    {
        await PinHandlesAsync();

        var snapshot = new ContractSnapshot(Data);
        var en = Data.EnglishStudent;
        var ar = Data.ArabicStudent;

        using var host = await _host.SignedInAsync(en);
        using var guest = await _host.SignedInAsync(ar);

        var path = new
        {
            gradeId = Data.GradeId,
            termId = Data.FirstTermId,
            subjectId = Data.ScienceId,
            chapterId = Data.MatterChapterId,
            lessonId = Data.SolidsLessonId
        };

        // --- a direct match: create, confirm, join, heartbeat, start --------------------------
        var created = await snapshot.PostAsync(host, "/api/multiplayer/sessions", new
        {
            gameId = Data.GameId,
            transportSessionName = "contract_room_one",
            transportRegion = "eu",
            maxPlayers = 2,
            isRanked = false,
            protocolVersion = 1,
            curriculumPath = path,
            requestId = "contract-create-1"
        }, "student-en");

        var sessionId = created!["id"]!.GetValue<string>();
        var session = $"/api/multiplayer/sessions/{sessionId}";

        await snapshot.PostAsync(host, $"{session}/start", new { requestId = "contract-confirm-1" }, "student-en");
        await snapshot.PostAsync(guest, $"{session}/join", new { protocolVersion = 1, requestId = "contract-join-1" }, "student-ar");

        await snapshot.PostAsync(host, $"{session}/heartbeat", new
        {
            connectedUserIds = new[] { en.Id, ar.Id },
            state = "Created"
        }, "student-en");

        await snapshot.GetAsync(host, session, "student-en");
        await snapshot.GetAsync(guest, $"{session}/players", "student-ar");
        await snapshot.GetAsync(guest, "/api/multiplayer/sessions", "student-ar");

        await snapshot.PostAsync(host, $"{session}/start", new { requestId = "contract-start-1" }, "student-en");

        // The guest is not the host: the refusal a returning stale host also receives.
        await snapshot.PostAsync(guest, $"{session}/heartbeat", new { connectedUserIds = new[] { ar.Id } }, "student-ar");

        // --- leaving and closing are idempotent -------------------------------------------------
        await snapshot.PostAsync(guest, $"{session}/leave", new { requestId = "contract-leave-1" }, "student-ar");
        await snapshot.PostAsync(guest, $"{session}/leave", new { requestId = "contract-leave-2" }, "student-ar");
        await snapshot.PostAsync(host, $"{session}/close", new { reason = "HostClosed", requestId = "contract-close-1" }, "student-en");
        await snapshot.PostAsync(host, $"{session}/close", new { reason = "HostClosed", requestId = "contract-close-2" }, "student-en");
        await snapshot.PostAsync(guest, $"{session}/join", new { protocolVersion = 1 }, "student-ar");

        // --- matchmaking: nothing open, then create, then join what the other created ----------
        await snapshot.PostAsync(host, "/api/multiplayer/matchmaking", new
        {
            gameId = Data.GameId,
            protocolVersion = 1,
            curriculumPath = new { lessonId = Data.SolidsLessonId },
            createIfNoneFound = false
        }, "student-en");

        var made = await snapshot.PostAsync(host, "/api/multiplayer/matchmaking", new
        {
            gameId = Data.GameId,
            protocolVersion = 1,
            maxPlayers = 2,
            curriculumPath = new { lessonId = Data.SolidsLessonId },
            createIfNoneFound = true,
            transportSessionName = "contract_room_two",
            requestId = "contract-matchmake-1"
        }, "student-en");

        var matchId = made!["session"]!["id"]!.GetValue<string>();
        var match = $"/api/multiplayer/sessions/{matchId}";

        await snapshot.PostAsync(host, $"{match}/start", new { requestId = "contract-confirm-2" }, "student-en");

        await snapshot.PostAsync(guest, "/api/multiplayer/matchmaking", new
        {
            gameId = Data.GameId,
            protocolVersion = 1,
            curriculumPath = new { lessonId = Data.SolidsLessonId },
            createIfNoneFound = true,
            transportSessionName = "contract_room_three",
            requestId = "contract-matchmake-2"
        }, "student-ar");

        // --- host migration, then the new host ends it ------------------------------------------
        await snapshot.PostAsync(host, $"{match}/host-transfer", new { toUserId = ar.Id, reason = "Voluntary" }, "student-en");
        await snapshot.PostAsync(host, $"{match}/close", new { }, "student-en");
        await snapshot.PostAsync(guest, $"{match}/close", new { requestId = "contract-close-3" }, "student-ar");

        // --- refusals the client maps -------------------------------------------------------------
        await snapshot.PostAsync(host, "/api/multiplayer/sessions", new
        {
            gameId = Data.GameId,
            transportSessionName = "contract_room_four",
            protocolVersion = 99
        }, "student-en");

        // A session id nobody holds a seat in answers exactly like one that does not exist.
        await snapshot.GetAsync(host, "/api/multiplayer/sessions/7d0c1c52-0000-4000-8000-00000000c0de", "student-en");
        await snapshot.GetAsync(_host.Http, session, "anonymous");

        snapshot.Verify("multiplayer");
    }

    [Fact]
    public async Task A_private_session_carries_a_join_code()
    {
        using var host = await _host.SignedInAsync(Data.EnglishStudent);

        var response = await host.PostAsync("/api/multiplayer/sessions", System.Net.Http.Json.JsonContent.Create(new
        {
            gameId = Data.GameId,
            transportSessionName = "contract_private_room",
            visibility = "Private",
            protocolVersion = 1
        }));

        var body = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(response.Content);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Private", body!["visibility"]!.GetValue<string>());

        var code = body["joinCode"]!.GetValue<string>();
        Assert.Equal(6, code.Length);

        // The host's room came up; until it is confirmed nobody can be seated, by code or otherwise.
        await host.PostAsync($"/api/multiplayer/sessions/{body["id"]}/start", System.Net.Http.Json.JsonContent.Create(new { }));

        // The friend's side, over the wire: the code in the body, typed the way people type it.
        using var friend = await _host.SignedInAsync(Data.ArabicStudent);

        var joined = await friend.PostAsync("/api/multiplayer/sessions/join-by-code", System.Net.Http.Json.JsonContent.Create(new
        {
            joinCode = code.ToLowerInvariant(),
            protocolVersion = 1
        }));

        var seat = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(joined.Content);
        Assert.Equal(System.Net.HttpStatusCode.OK, joined.StatusCode);
        Assert.Equal(body["id"]!.GetValue<string>(), seat!["id"]!.GetValue<string>());

        var unknown = await friend.PostAsync("/api/multiplayer/sessions/join-by-code", System.Net.Http.Json.JsonContent.Create(new
        {
            joinCode = "ZZZZZZ",
            protocolVersion = 1
        }));

        var refusal = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(unknown.Content);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("SESSION_NOT_FOUND", refusal!["code"]!.GetValue<string>());

        // The host removes the friend; the code no longer lets them back in.
        var removed = await host.PostAsync($"/api/multiplayer/sessions/{body["id"]}/remove", System.Net.Http.Json.JsonContent.Create(new
        {
            userId = Data.ArabicStudent.Id
        }));
        Assert.Equal(System.Net.HttpStatusCode.OK, removed.StatusCode);

        var readmit = await friend.PostAsync("/api/multiplayer/sessions/join-by-code", System.Net.Http.Json.JsonContent.Create(new
        {
            joinCode = code,
            protocolVersion = 1
        }));

        var shut = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(readmit.Content);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, readmit.StatusCode);
        Assert.Equal("SESSION_REMOVED", shut!["code"]!.GetValue<string>());

        // Leave nothing live behind for the other scenarios sharing this host.
        await host.PostAsync($"/api/multiplayer/sessions/{body["id"]}/close", System.Net.Http.Json.JsonContent.Create(new { }));
    }

    /// <summary>
    /// Fixed public handles for the two fixture students, so the roster is deterministic whichever
    /// name source the server uses. Written once; a second call finds them already there.
    /// </summary>
    private async Task PinHandlesAsync()
    {
        await using var context = new Share7.Infrastructure.Persistence.ApplicationDbContext(
            new DbContextOptionsBuilder<Share7.Infrastructure.Persistence.ApplicationDbContext>()
                .UseSqlServer(_host.ConnectionString)
                .Options);

        foreach (var (userId, handle) in new[] { (Data.EnglishStudent.Id, "ContractOwl11"), (Data.ArabicStudent.Id, "ContractFox22") })
        {
            if (await context.PlayerDisplayNames.AnyAsync(n => n.UserId == userId))
                continue;

            context.PlayerDisplayNames.Add(new PlayerDisplayName
            {
                UserId = userId,
                Handle = handle,
                Source = DisplayNameSource.Generated,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
    }
}
