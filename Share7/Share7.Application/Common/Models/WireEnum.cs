using System.Text.RegularExpressions;

namespace Share7.Application.Common.Models;

/// <summary>
/// Converts enum members to and from their <c>SCREAMING_SNAKE_CASE</c> wire form.
/// <para>
/// One implementation on purpose. The same vocabulary has to appear in three places — the column
/// the ledger is stored in, the JSON the client reads, and the JSON an admin writes — and three
/// copies of this mapping is how <c>LESSON_COMPLETED</c> ends up meaning one thing in the database
/// and another on the wire.
/// </para>
/// <para>
/// Derived from the member name rather than a hand-maintained table, so renaming an enum member
/// changes the wire form with it and cannot silently disagree.
/// </para>
/// </summary>
public static class WireEnum
{
    public static string ToWire<TEnum>(TEnum value) where TEnum : struct, Enum =>
        ToWire(value.ToString());

    public static string ToWire(string memberName) =>
        Regex.Replace(memberName, "(?<=[a-z0-9])([A-Z])", "_$1").ToUpperInvariant();

    /// <summary>
    /// Parses the wire form back. Tolerant on input — underscores are optional and case is
    /// ignored, so <c>LESSON_COMPLETED</c>, <c>lesson_completed</c> and <c>LessonCompleted</c> all
    /// resolve. Output is always canonical.
    /// </summary>
    public static bool TryFromWire<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
    {
        value = default;

        return !string.IsNullOrWhiteSpace(text)
               && Enum.TryParse(text.Replace("_", string.Empty), ignoreCase: true, out value)
               && Enum.IsDefined(value);
    }

    /// <summary>
    /// Parses, falling back to <c>default</c> for anything unrecognised.
    /// <para>
    /// Used when reading stored values, where a row written by a newer deployment must not throw
    /// on an older one. Do **not** use it on user input — a typo becomes <c>Unknown</c> silently.
    /// Use <see cref="TryFromWire{TEnum}"/> there.
    /// </para>
    /// </summary>
    public static TEnum FromWire<TEnum>(string text) where TEnum : struct, Enum =>
        TryFromWire<TEnum>(text, out var parsed) ? parsed : default;
}
