using System.Security.Cryptography;
using System.Text;

namespace Share7.Infrastructure.Seeding;

/// <summary>
/// Turns a seed's own vocabulary — <c>"chapter:primary_three:t1:math:2"</c> — into a stable Guid.
/// <para>
/// <b>Why not <c>Guid.NewGuid()</c>.</b> The seeder has to be re-runnable and it has to be the same
/// across environments. Random ids give neither: a second run would have no way to recognise its own
/// rows, and staging and production would disagree about the id of the same lesson, so a bug report
/// naming an id would be unresolvable anywhere but the machine it came from.
/// </para>
/// <para>
/// This is RFC 4122 version 5 (SHA-1, name-based) under a namespace private to Share7 seeding, which
/// is what makes a name collide only with itself.
/// </para>
/// </summary>
internal static class SeedId
{
    /// <summary>
    /// Namespace for every id this seeder mints. Changing it re-keys the entire seed — a new run
    /// would insert a second copy of everything rather than recognising what is there. It is a
    /// constant for that reason, not a configuration knob.
    /// </summary>
    private static readonly Guid Namespace = Guid.Parse("5ea7d001-0000-4000-8000-5eedc0ffee00");

    /// <summary>The version-5 Guid for <paramref name="name"/> under the Share7 seeding namespace.</summary>
    public static Guid For(string name)
    {
        var namespaceBytes = Namespace.ToByteArray();
        SwapToBigEndian(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

        var hash = SHA1.HashData(input);

        var result = new byte[16];
        Buffer.BlockCopy(hash, 0, result, 0, 16);

        // Version 5 in the high nibble of byte 6, RFC 4122 variant in the top bits of byte 8.
        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        SwapToBigEndian(result);
        return new Guid(result);
    }

    /// <summary>Composes a name from parts, so callers cannot accidentally build two spellings of one key.</summary>
    public static Guid For(params string[] parts) => For(string.Join(':', parts));

    /// <summary>
    /// .NET lays the first three Guid fields out little-endian; RFC 4122 hashes them big-endian.
    /// Without this the ids would still be stable, just not the ones any other implementation
    /// computes for the same name.
    /// </summary>
    private static void SwapToBigEndian(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
