using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Share7.Application.Auth.Models;

namespace Share7.Infrastructure.Identity.ExternalAuth;

public class FacebookLoginValidator : IExternalLoginValidator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public FacebookLoginValidator(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public string Provider => ExternalAuthProvider.Facebook;

    public async Task<ExternalUserInfo?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(FacebookLoginValidator));

        if (!await IsTokenForThisAppAsync(client, token, cancellationToken))
            return null;

        var response = await client.GetAsync(
            $"https://graph.facebook.com/me?fields=id,name,email&access_token={Uri.EscapeDataString(token)}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var payload = await response.Content.ReadFromJsonAsync<FacebookMeResponse>(cancellationToken: cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Email))
            return null;

        return new ExternalUserInfo(payload.Id, payload.Email, payload.Name);
    }

    private async Task<bool> IsTokenForThisAppAsync(HttpClient client, string token, CancellationToken cancellationToken)
    {
        var appId = _configuration["Authentication:Facebook:AppId"];
        var appSecret = _configuration["Authentication:Facebook:AppSecret"];

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
            return true;

        var appAccessToken = $"{appId}|{appSecret}";
        var response = await client.GetAsync(
            $"https://graph.facebook.com/debug_token?input_token={Uri.EscapeDataString(token)}&access_token={Uri.EscapeDataString(appAccessToken)}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return false;

        var debugResult = await response.Content.ReadFromJsonAsync<FacebookDebugTokenResponse>(cancellationToken: cancellationToken);
        return debugResult?.Data is { IsValid: true } data && string.Equals(data.AppId, appId, StringComparison.Ordinal);
    }

    private sealed class FacebookMeResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    private sealed class FacebookDebugTokenResponse
    {
        [JsonPropertyName("data")]
        public FacebookDebugTokenData? Data { get; set; }
    }

    private sealed class FacebookDebugTokenData
    {
        [JsonPropertyName("app_id")]
        public string? AppId { get; set; }

        [JsonPropertyName("is_valid")]
        public bool IsValid { get; set; }
    }
}
