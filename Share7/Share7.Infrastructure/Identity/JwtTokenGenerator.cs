using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Share7.Application.Auth.Interfaces;
using Share7.Domain.Constants;

namespace Share7.Infrastructure.Identity;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtSettings _settings;

    public JwtTokenGenerator(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
    }

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(
        Guid userId,
        string username,
        string? email,
        IEnumerable<string> roles,
        Guid? preferredLanguageId = null)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrWhiteSpace(email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));

        // Lets content endpoints resolve the language tree without a per-request database hit.
        if (preferredLanguageId is not null)
            claims.Add(new Claim(CustomClaimTypes.PreferredLanguage, preferredLanguageId.Value.ToString()));

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return (accessToken, expiresAt);
    }

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
