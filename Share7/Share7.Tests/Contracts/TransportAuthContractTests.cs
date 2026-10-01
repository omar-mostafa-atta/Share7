using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Share7.Tests.Contracts;

/// <summary>
/// Photon custom authentication over the real wire: the reply is in Photon's field names, the
/// callback needs no sign-in, and a ticket opens nothing but Photon.
/// </summary>
[Collection(ContractCollection.Name)]
public class TransportAuthContractTests
{
    private readonly ContractHost _host;

    public TransportAuthContractTests(ContractHost host) => _host = host;

    private async Task<string> TicketAsync(HttpClient student)
    {
        var issued = await student.PostAsync("/api/multiplayer/transport/ticket", content: null);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);

        var body = await issued.Content.ReadFromJsonAsync<JsonObject>();
        Assert.Equal("photon", body!["provider"]!.GetValue<string>());

        return body["ticket"]!.GetValue<string>();
    }

    [Fact]
    public async Task Photon_is_answered_in_its_own_words_without_signing_in()
    {
        using var student = await _host.SignedInAsync(_host.Data.EnglishStudent);
        var ticket = await TicketAsync(student);

        // Photon's call carries no Share7 credentials — it is the ticket that identifies the player.
        var viaQuery = await _host.Http.GetAsync($"/api/multiplayer/transport/photon-auth?ticket={Uri.EscapeDataString(ticket)}");
        var viaPost = await _host.Http.PostAsJsonAsync("/api/multiplayer/transport/photon-auth", new { ticket });

        foreach (var response in new[] { viaQuery, viaPost })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var raw = await response.Content.ReadAsStringAsync();
            var body = JsonNode.Parse(raw)!.AsObject();

            // Photon reads these exact, PascalCase names; the API's camel-casing must not reach them.
            // Checked on the raw text: a parsed JsonObject may match names case-insensitively.
            Assert.Contains("\"ResultCode\":", raw);
            Assert.DoesNotContain("\"resultCode\"", raw);
            Assert.Equal(1, body["ResultCode"]!.GetValue<int>());
            Assert.False(string.IsNullOrEmpty(body["UserId"]?.GetValue<string>()));
            Assert.False(string.IsNullOrEmpty(body["Nickname"]?.GetValue<string>()));
        }
    }

    [Fact]
    public async Task A_refusal_is_still_a_200_so_Photon_reads_it_as_a_refusal()
    {
        var response = await _host.Http.GetAsync("/api/multiplayer/transport/photon-auth?ticket=forged");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<JsonObject>())!["ResultCode"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_ticket_is_not_an_access_token()
    {
        using var student = await _host.SignedInAsync(_host.Data.EnglishStudent);
        var ticket = await TicketAsync(student);

        using var withTicket = new HttpClient { BaseAddress = _host.Http.BaseAddress };
        withTicket.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ticket);

        // What leaks through Photon's servers must be worth a Photon connection and nothing else.
        var response = await withTicket.GetAsync("/api/multiplayer/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Only_a_signed_in_player_gets_a_ticket()
    {
        var response = await _host.Http.PostAsync("/api/multiplayer/transport/ticket", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
