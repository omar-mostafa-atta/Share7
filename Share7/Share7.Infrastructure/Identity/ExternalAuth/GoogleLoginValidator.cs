using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Share7.Application.Auth.Models;

namespace Share7.Infrastructure.Identity.ExternalAuth;

public class GoogleLoginValidator : IExternalLoginValidator
{
    private readonly IConfiguration _configuration;

    public GoogleLoginValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Provider => ExternalAuthProvider.Google;

    public async Task<ExternalUserInfo?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var clientId = _configuration["Authentication:Google:ClientId"];

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = string.IsNullOrWhiteSpace(clientId) ? null : [clientId]
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(token, settings);
            if (string.IsNullOrWhiteSpace(payload.Email))
                return null;

            // Google states whether it has verified the address. Carried through so an unverified
            // one can never be used to take over an existing account that happens to share it.
            return new ExternalUserInfo(payload.Subject, payload.Email, payload.Name, payload.EmailVerified);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
