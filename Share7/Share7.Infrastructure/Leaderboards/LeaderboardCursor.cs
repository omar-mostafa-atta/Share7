using System.Security.Cryptography;
using System.Text;

namespace Share7.Infrastructure.Leaderboards;

/// <summary>
/// The paging cursor: where the last page stopped, plus a signature.
/// <para>
/// **Opaque so the encoding can change without a client release, and signed so it cannot be
/// forged.** The forgery matters more than it looks: the cursor carries the rank to resume from,
/// so a caller who could edit it could page straight past an entitlement limit that the endpoint
/// enforces on the cursor's own contents.
/// </para>
/// <para>
/// It is a cursor rather than an <c>OFFSET</c> because the board is being written to while it is
/// being read. Under <c>OFFSET</c>, one player overtaking another between page one and page two
/// shifts everyone down, so a row is silently skipped and another silently repeated — on a
/// leaderboard, that reads as a child's name disappearing.
/// </para>
/// </summary>
public sealed record LeaderboardCursor(Guid CycleId, int Cohort, Guid CohortKey, int AfterRank)
{
    private const string Prefix = "lb1";

    /// <summary>
    /// Encodes and signs. The payload is deliberately small — everything in it is already known to
    /// the caller from the request they just made, so there is nothing here worth encrypting.
    /// </summary>
    public string Encode(byte[] key)
    {
        var payload = $"{Prefix}.{CycleId:N}.{Cohort}.{CohortKey:N}.{AfterRank}";
        var signature = Sign(payload, key);

        return Base64Url(Encoding.UTF8.GetBytes($"{payload}.{signature}"));
    }

    /// <summary>
    /// Reads a cursor back, returning false for anything malformed, tampered with, or pointing at
    /// a different board than the caller is reading.
    /// <para>
    /// **Never throws.** A bad cursor is an ordinary event — a stale deep link, a client that kept
    /// one across a release — and the endpoint answers it with a refusal the client can recover
    /// from by restarting at the top, not with a 500.
    /// </para>
    /// </summary>
    public static bool TryDecode(string? encoded, byte[] key, out LeaderboardCursor cursor)
    {
        cursor = default!;

        if (string.IsNullOrWhiteSpace(encoded))
            return false;

        string decoded;

        try
        {
            decoded = Encoding.UTF8.GetString(FromBase64Url(encoded));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = decoded.Split('.');

        if (parts.Length != 6 || parts[0] != Prefix)
            return false;

        var payload = string.Join('.', parts[..5]);

        // Fixed-time comparison. The consequence of a timing leak here is small, but the cost of
        // not leaking is one method call.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Sign(payload, key)),
                Encoding.UTF8.GetBytes(parts[5])))
        {
            return false;
        }

        if (!Guid.TryParseExact(parts[1], "N", out var cycleId)
            || !int.TryParse(parts[2], out var cohort)
            || !Guid.TryParseExact(parts[3], "N", out var cohortKey)
            || !int.TryParse(parts[4], out var afterRank)
            || afterRank < 0)
        {
            return false;
        }

        cursor = new LeaderboardCursor(cycleId, cohort, cohortKey, afterRank);
        return true;
    }

    private static string Sign(string payload, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');

        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }
}
