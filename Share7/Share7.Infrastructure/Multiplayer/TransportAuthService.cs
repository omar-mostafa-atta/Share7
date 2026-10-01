using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Share7.Application.Common.Models;
using Share7.Application.Multiplayer.Interfaces;
using Share7.Application.Multiplayer.Models;
using Share7.Infrastructure.Identity;
using Share7.Infrastructure.Persistence;

namespace Share7.Infrastructure.Multiplayer;

/// <summary>
/// Transport tickets: signed like access tokens, but with their **own audience and a derived key**,
/// so a ticket is refused by every API route and an access token is refused here. That matters
/// because a ticket travels through Photon's servers — what leaks there must be worth nothing but a
/// Photon connection, for two minutes.
/// </summary>
public class TransportAuthService : ITransportAuthService
{
    private const string AudienceSuffix = ".Transport";

    private readonly ApplicationDbContext _dbContext;
    private readonly IRosterNameResolver _names;
    private readonly JwtSettings _jwt;
    private readonly MultiplayerOptions _options;
    private readonly ILogger<TransportAuthService> _logger;

    public TransportAuthService(
        ApplicationDbContext dbContext,
        IRosterNameResolver names,
        IOptions<JwtSettings> jwt,
        IOptions<MultiplayerOptions> options,
        ILogger<TransportAuthService> logger)
    {
        _dbContext = dbContext;
        _names = names;
        _jwt = jwt.Value;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Derived from the platform secret, like the Studio's, so no new secret has to be managed.</summary>
    private static byte[] SigningKey(string platformSecret) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(platformSecret), Encoding.UTF8.GetBytes("share7.transport.ticket.v1"));

    private string Audience => _jwt.Audience + AudienceSuffix;

    public async Task<ServiceResult<TransportTicketDto>> IssueTicketAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Checked here as well as when Photon asks, so a locked account learns it now rather than as
        // an unexplained connection failure.
        if (!await IsUsableAsync(userId, cancellationToken))
            return ServiceResult<TransportTicketDto>.Failure(
                ApiErrors.Forbidden, ServiceErrorKind.Forbidden, "This account cannot connect to multiplayer.");

        var now = DateTime.UtcNow;
        var expires = now.AddSeconds(Math.Max(10, _options.TransportTicketSeconds));

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],

            // No "not before", for the reason the Studio's tokens have none: zero skew would refuse a
            // ticket on a clock a second behind the one that minted it.
            notBefore: null,
            expires: expires,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(SigningKey(_jwt.Secret)), SecurityAlgorithms.HmacSha256));

        MultiplayerMetrics.TransportAuth.Add(1, MultiplayerMetrics.Tag("outcome", "issued"));

        return ServiceResult<TransportTicketDto>.Success(new TransportTicketDto
        {
            Ticket = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAtUtc = expires,
            Provider = TransportProviders.Photon
        });
    }

    public async Task<PhotonAuthResponse> AuthenticatePhotonAsync(
        string? ticket,
        string? key,
        CancellationToken cancellationToken = default)
    {
        // Cheapest check first: a caller that is not Photon is turned away before any signature or
        // database work, since this route cannot be rate limited per address (all of Photon's calls
        // come from a handful of its servers).
        if (!string.IsNullOrEmpty(_options.PhotonAuthKey) && !SameSecret(key, _options.PhotonAuthKey))
            return Refuse("bad_key", "Not authorised.");

        if (string.IsNullOrWhiteSpace(ticket))
            return Answer(PhotonAuthResponse.Malformed, "missing_ticket", "Missing ticket.");

        if (ReadTicket(ticket) is not { } userId)
            return Refuse("invalid_ticket", "Ticket is invalid or expired.");

        if (!await IsUsableAsync(userId, cancellationToken))
            return Refuse("account_unusable", "This account cannot connect to multiplayer.");

        var names = await _names.ResolveAsync([userId], cancellationToken);

        MultiplayerMetrics.TransportAuth.Add(1, MultiplayerMetrics.Tag("outcome", "accepted"));

        return new PhotonAuthResponse
        {
            ResultCode = PhotonAuthResponse.Succeeded,
            UserId = userId.ToString(),
            Nickname = names.GetValueOrDefault(userId)
        };
    }

    /// <summary>The ticket's user, or null when it is forged, expired, or not a transport ticket.</summary>
    private Guid? ReadTicket(string ticket)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _jwt.Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = new SymmetricSecurityKey(SigningKey(_jwt.Secret)),
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(ticket, parameters, out _);

            return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) ? userId : null;
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

    /// <summary>The account still exists and is not locked out — deleted accounts have no row.</summary>
    private Task<bool> IsUsableAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && !(u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > now), cancellationToken);
    }

    private static bool SameSecret(string? given, string expected)
    {
        if (given is null)
            return false;

        var a = SHA256.HashData(Encoding.UTF8.GetBytes(given));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private PhotonAuthResponse Refuse(string reason, string message) =>
        Answer(PhotonAuthResponse.Refused, reason, message);

    private PhotonAuthResponse Answer(int code, string reason, string message)
    {
        MultiplayerMetrics.TransportAuth.Add(1, MultiplayerMetrics.Tag("outcome", reason));
        _logger.LogInformation("Photon authentication refused ({Reason}).", reason);

        return new PhotonAuthResponse { ResultCode = code, Message = message };
    }
}
