using System.Text.Json.Serialization;

namespace Share7.Application.Multiplayer.Models;

/// <summary>
/// What a client passes to Photon as its custom-authentication value, so that only a signed-in
/// Share7 account can connect to the realtime transport at all.
/// </summary>
public class TransportTicketDto
{
    /// <summary>Opaque. Pass it to Photon unchanged as the <c>ticket</c> auth parameter.</summary>
    public string Ticket { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Always <c>photon</c> today — named so a second transport is an additive change.</summary>
    public string Provider { get; set; } = TransportProviders.Photon;
}

public static class TransportProviders
{
    public const string Photon = "photon";
}

/// <summary>
/// Photon's custom-authentication reply, in **Photon's own field names** — its servers read exactly
/// these, so they are pinned here rather than left to the API's camel-casing.
/// <para>
/// Always sent with HTTP 200: Photon reads a non-200 as its own failure to reach the service, not
/// as the account being refused.
/// </para>
/// </summary>
public class PhotonAuthResponse
{
    /// <summary>1 = authenticated, 2 = refused, 3 = the call was malformed.</summary>
    [JsonPropertyName("ResultCode")]
    public int ResultCode { get; set; }

    /// <summary>
    /// The Share7 user id. Photon uses it as the peer's <c>UserId</c>, which is what lets a host
    /// match every peer in its room against the backend roster.
    /// </summary>
    [JsonPropertyName("UserId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; set; }

    /// <summary>The player's public handle, so no client can choose what other children see.</summary>
    [JsonPropertyName("Nickname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Nickname { get; set; }

    [JsonPropertyName("Message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    public const int Succeeded = 1;
    public const int Refused = 2;
    public const int Malformed = 3;
}

/// <summary>The body Photon posts when a client sends its auth values as post data.</summary>
public class PhotonAuthRequest
{
    public string? Ticket { get; set; }
}
