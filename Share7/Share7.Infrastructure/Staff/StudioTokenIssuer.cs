using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Share7.Infrastructure.Identity;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// Mints and reads the Studio's two kinds of token: the short access token every Studio request
/// carries, and the five-minute challenge that sits between a correct password and a 2-step code.
/// <para>
/// Both are signed with <see cref="StaffSecrets.StudioSigningKey"/>, and each has its own audience,
/// so neither can be passed off as the other — nor as a game or admin token, nor the reverse.
/// </para>
/// </summary>
public class StudioTokenIssuer
{
    public const string ChallengeAudienceSuffix = ".TwoStep";

    private readonly JwtSettings _jwt;
    private readonly StudioOptions _studio;

    public StudioTokenIssuer(IOptions<JwtSettings> jwt, IOptions<StudioOptions> studio)
    {
        _jwt = jwt.Value;
        _studio = studio.Value;
    }

    /// <summary>What the Studio authentication scheme validates against — the one definition.</summary>
    public static TokenValidationParameters ValidationParameters(JwtSettings jwt, StudioOptions studio) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = studio.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(StaffSecrets.StudioSigningKey(jwt.Secret)),
        ClockSkew = TimeSpan.Zero,

        // Keep "sub" and "sid" as written rather than remapped to long URI claim types.
        NameClaimType = JwtRegisteredClaimNames.UniqueName,
        RoleClaimType = StaffSecrets.StudioRoleClaim
    };

    public (string Token, DateTime ExpiresAtUtc) AccessToken(Guid userId, string username, Guid sessionId, string stampHash, DateTime nowUtc)
    {
        var expires = nowUtc.AddMinutes(_studio.AccessTokenMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(StaffSecrets.SessionClaim, sessionId.ToString()),
            new Claim(StaffSecrets.StampClaim, stampHash),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        return (Write(claims, _studio.Audience, nowUtc, expires), expires);
    }

    public (string Token, DateTime ExpiresAtUtc) TwoStepChallenge(Guid userId, string stampHash, DateTime nowUtc)
    {
        var expires = nowUtc.Add(StaffSecrets.TwoStepChallengeLifetime);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(StaffSecrets.StampClaim, stampHash),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        return (Write(claims, _studio.Audience + ChallengeAudienceSuffix, nowUtc, expires), expires);
    }

    /// <summary>The challenge's user and stamp fingerprint, or null when it is forged, expired or not a challenge.</summary>
    public (Guid UserId, string StampHash)? ReadTwoStepChallenge(string? challenge)
    {
        if (string.IsNullOrWhiteSpace(challenge))
            return null;

        var parameters = ValidationParameters(_jwt, _studio);
        parameters.ValidAudience = _studio.Audience + ChallengeAudienceSuffix;

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(challenge, parameters, out _);

            return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)
                   && principal.FindFirstValue(StaffSecrets.StampClaim) is { } stamp
                ? (userId, stamp)
                : null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Not a JWT at all.
            return null;
        }
    }

    private string Write(IEnumerable<Claim> claims, string audience, DateTime nowUtc, DateTime expiresUtc)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(StaffSecrets.StudioSigningKey(_jwt.Secret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: audience,
            claims: claims,

            // No "not before": with zero clock skew it would refuse a token on a server whose clock
            // runs a second behind the one that issued it.
            notBefore: null,
            expires: expiresUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
