using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Share7.Application.Multiplayer.Models;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Multiplayer;
using Share7.Infrastructure.Persistence;
using Share7.Tests.Infrastructure;
using Xunit;
using MSOptions = Microsoft.Extensions.Options.Options;

namespace Share7.Tests;

/// <summary>
/// Photon custom authentication: only an account the backend vouched for moments ago connects to the
/// realtime transport, under its own user id and public handle (threat T1).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class TransportAuthTests
{
    private static readonly JwtSettings Jwt = new()
    {
        Secret = new string('t', 64),
        Issuer = "share7-tests",
        Audience = "share7-game"
    };

    private readonly SqlServerFixture _fixture;

    public TransportAuthTests(SqlServerFixture fixture) => _fixture = fixture;

    private static TransportAuthService Service(ApplicationDbContext context, Action<MultiplayerOptions>? configure = null)
    {
        var options = MSOptions.Create(MultiplayerTest.Options(configure));

        return new TransportAuthService(
            context,
            MultiplayerTest.Names(context, options),
            MSOptions.Create(Jwt),
            options,
            NullLogger<TransportAuthService>.Instance);
    }

    /// <summary>A token signed the way this suite's secret signs things, with any audience and lifetime.</summary>
    private static string Token(Guid userId, byte[] key, string audience, DateTime expires) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: Jwt.Issuer,
            audience: audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            notBefore: null,
            expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)));

    private static byte[] TicketKey =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(Jwt.Secret), Encoding.UTF8.GetBytes("share7.transport.ticket.v1"));

    [Fact]
    public async Task A_ticket_lets_its_owner_in_under_their_id_and_public_handle()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var transport = Service(context);

        var ticket = await transport.IssueTicketAsync(userId);
        Assert.True(ticket.Succeeded, ticket.Error?.Code);
        Assert.Equal("photon", ticket.Value!.Provider);

        var answer = await transport.AuthenticatePhotonAsync(ticket.Value.Ticket, key: null);

        Assert.Equal(PhotonAuthResponse.Succeeded, answer.ResultCode);
        Assert.Equal(userId.ToString(), answer.UserId);

        // The same handle the roster shows — never a name a client chose, never a real name.
        var handles = await MultiplayerTest.Names(context, MSOptions.Create(MultiplayerTest.Options())).ResolveAsync([userId]);
        Assert.Equal(handles[userId], answer.Nickname);
    }

    [Fact]
    public async Task An_access_token_is_not_a_ticket()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        // Signed with the platform key for the game's audience — exactly what an access token is.
        var accessToken = Token(userId, Encoding.UTF8.GetBytes(Jwt.Secret), Jwt.Audience, DateTime.UtcNow.AddMinutes(15));

        var answer = await Service(context).AuthenticatePhotonAsync(accessToken, key: null);

        Assert.Equal(PhotonAuthResponse.Refused, answer.ResultCode);
        Assert.Null(answer.UserId);
    }

    [Fact]
    public async Task An_expired_ticket_is_refused()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);

        var stale = Token(userId, TicketKey, Jwt.Audience + ".Transport", DateTime.UtcNow.AddSeconds(-1));

        Assert.Equal(PhotonAuthResponse.Refused, (await Service(context).AuthenticatePhotonAsync(stale, key: null)).ResultCode);
    }

    [Theory]
    [InlineData("not-a-ticket")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.e30.c2lnbmF0dXJl")]
    public async Task Anything_that_is_not_a_ticket_is_refused(string ticket)
    {
        await using var context = _fixture.CreateContext();

        Assert.Equal(PhotonAuthResponse.Refused, (await Service(context).AuthenticatePhotonAsync(ticket, key: null)).ResultCode);
    }

    [Fact]
    public async Task A_call_without_a_ticket_is_malformed()
    {
        await using var context = _fixture.CreateContext();

        Assert.Equal(PhotonAuthResponse.Malformed, (await Service(context).AuthenticatePhotonAsync(" ", key: null)).ResultCode);
    }

    [Fact]
    public async Task A_locked_account_neither_gets_a_ticket_nor_uses_one_it_already_had()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var transport = Service(context);

        var ticket = await transport.IssueTicketAsync(userId);

        await context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(u => u.LockoutEnabled, true)
                .SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddDays(1)));

        Assert.Equal(PhotonAuthResponse.Refused, (await transport.AuthenticatePhotonAsync(ticket.Value!.Ticket, key: null)).ResultCode);

        var refused = await transport.IssueTicketAsync(userId);
        Assert.False(refused.Succeeded);
        Assert.Equal("FORBIDDEN", refused.Error!.Code);
    }

    [Fact]
    public async Task With_a_key_configured_only_a_caller_holding_it_is_answered()
    {
        await using var context = _fixture.CreateContext();
        var userId = await TestData.CreateUserAsync(context);
        var transport = Service(context, o => o.PhotonAuthKey = "photon-dashboard-key");

        var ticket = (await transport.IssueTicketAsync(userId)).Value!.Ticket;

        Assert.Equal(PhotonAuthResponse.Refused, (await transport.AuthenticatePhotonAsync(ticket, key: null)).ResultCode);
        Assert.Equal(PhotonAuthResponse.Refused, (await transport.AuthenticatePhotonAsync(ticket, key: "guess")).ResultCode);
        Assert.Equal(PhotonAuthResponse.Succeeded, (await transport.AuthenticatePhotonAsync(ticket, key: "photon-dashboard-key")).ResultCode);
    }
}
