using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// The Studio's configuration section, <c>"Studio"</c>.
/// </summary>
public class StudioOptions
{
    public const string SectionName = "Studio";

    /// <summary>
    /// The Studio's address, e.g. <c>https://shareh.runasp.net/studio</c> — the one fixed address the
    /// content team signs in at, shown to whoever creates a member. When empty the Admin Console
    /// shows <c>/studio</c> on its own address, where the API serves the Studio (StudioHosting).
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>The token audience. Game and admin tokens carry a different one and are refused on /api/studio.</summary>
    public string Audience { get; set; } = "Share7.Studio";

    public int AccessTokenMinutes { get; set; } = 15;
}

/// <summary>Secrets, hashes and the few constants every staff-security class shares.</summary>
public static class StaffSecrets
{
    /// <summary>The Studio's refresh cookie. HttpOnly, Secure, SameSite=Strict, scoped to the auth routes.</summary>
    public const string RefreshCookieName = "s7_studio_rt";
    public const string RefreshCookiePath = "/api/studio/auth";

    /// <summary>Claim naming the <c>StaffSession</c> a Studio token belongs to.</summary>
    public const string SessionClaim = "sid";

    /// <summary>Claim carrying a hash of the account's security stamp when the token was issued.</summary>
    public const string StampClaim = "sst";

    /// <summary>Added by the per-request check (never by the token): the member's Studio role.</summary>
    public const string StudioRoleClaim = "studio_role";

    /// <summary>Added by the per-request check: <c>full</c>, or <c>setup</c> while 2-step must be set up first.</summary>
    public const string StudioAccessClaim = "studio_access";

    public const string FullAccess = "full";
    public const string SetupOnlyAccess = "setup";

    /// <summary>How long after a rotation the previous refresh token is treated as a parallel tab rather than a copy.</summary>
    public static readonly TimeSpan RefreshReuseGrace = TimeSpan.FromSeconds(30);

    /// <summary>How long a 2-step challenge lasts between the password and the code.</summary>
    public static readonly TimeSpan TwoStepChallengeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long a per-request check may be answered from memory.</summary>
    public static readonly TimeSpan ValidationCacheWindow = TimeSpan.FromSeconds(60);

    /// <summary>256 random bits, URL-safe. Setup links and refresh tokens.</summary>
    public static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>Lower-case hex SHA-256 — what is stored in place of every secret.</summary>
    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    /// <summary>
    /// A short, one-way fingerprint of an Identity security stamp, for tokens. The stamp itself is
    /// never put in a token: Identity derives some one-time codes from it.
    /// </summary>
    public static string StampHash(string? securityStamp) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes("share7.stamp/" + (securityStamp ?? string.Empty))))[..22];

    /// <summary>
    /// The Studio's signing key, derived from the platform secret. A game or admin token can never
    /// verify as a Studio token even if an audience check were misconfigured, and no second secret
    /// has to be deployed.
    /// </summary>
    public static byte[] StudioSigningKey(string platformSecret) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(platformSecret), Encoding.UTF8.GetBytes("share7.studio.signing.v1"));

    /// <summary>Constant-time comparison for two hashes of equal kind.</summary>
    public static bool SameHash(string? a, string? b) =>
        a is not null && b is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}

/// <summary>
/// "Chrome on Windows" from a user-agent string — enough for a person to recognise their own
/// devices on a sessions list, and no more. Unknown agents read as null and are shown as such.
/// </summary>
public static class DeviceNames
{
    public static string? Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return null;

        var ua = userAgent;

        var browser =
            ua.Contains("Edg/", StringComparison.Ordinal) ? "Edge" :
            ua.Contains("OPR/", StringComparison.Ordinal) ? "Opera" :
            ua.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox" :
            ua.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome" :
            ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari" :
            null;

        var system =
            ua.Contains("Windows", StringComparison.Ordinal) ? "Windows" :
            ua.Contains("iPhone", StringComparison.Ordinal) || ua.Contains("iPad", StringComparison.Ordinal) ? "iOS" :
            ua.Contains("Android", StringComparison.Ordinal) ? "Android" :
            ua.Contains("Mac OS X", StringComparison.Ordinal) ? "macOS" :
            ua.Contains("CrOS", StringComparison.Ordinal) ? "ChromeOS" :
            ua.Contains("Linux", StringComparison.Ordinal) ? "Linux" :
            null;

        return (browser, system) switch
        {
            (null, null) => null,
            (null, { } os) => os,
            ({ } b, null) => b,
            ({ } b, { } os) => $"{b} on {os}"
        };
    }
}
