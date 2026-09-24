using Share7.Application.Staff.Models;

namespace Share7.Infrastructure.Staff;

/// <summary>
/// The rules a content-team password must meet, checked before Identity's own validators so the
/// Studio can show each broken rule by name, in either language.
/// <para>
/// Length is the rule that matters (at least 12, a SuperAdmin can raise it). The composition rules
/// are Identity's defaults for every account on the platform and are listed so the form can show
/// them up front instead of being refused by them afterwards. On top: not a well-known password,
/// and not built from the username or the product's own name — the first things anyone tries.
/// </para>
/// </summary>
public static class StaffPasswordPolicy
{
    public static StaffPasswordRulesDto Rules(int minimumLength) =>
        new(minimumLength, RequireUppercase: true, RequireLowercase: true, RequireDigit: true);

    /// <summary>The rules this password breaks, as the Studio's problem codes. Empty when it is acceptable.</summary>
    public static IReadOnlyList<string> Problems(string? password, string username, int minimumLength)
    {
        var problems = new List<string>();
        password ??= string.Empty;

        if (password.Length < minimumLength) problems.Add("tooShort");
        if (!password.Any(char.IsUpper)) problems.Add("needsUppercase");
        if (!password.Any(char.IsLower)) problems.Add("needsLowercase");
        if (!password.Any(char.IsDigit)) problems.Add("needsDigit");

        var lower = password.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(username) && username.Length >= 3 &&
            lower.Contains(username.ToLowerInvariant(), StringComparison.Ordinal))
        {
            problems.Add("containsUsername");
        }

        if (IsCommon(lower))
            problems.Add("common");

        return problems;
    }

    /// <summary>
    /// A well-known password, or one of them dressed up — capitalised, with digits and symbols on
    /// the end ("Password2026!"), or with the product's name in it. The dressing is exactly what
    /// guessing tools try first, so it earns no credit.
    /// </summary>
    private static bool IsCommon(string lower)
    {
        if (lower.Contains("share7", StringComparison.Ordinal) || lower.Contains("shareh", StringComparison.Ordinal))
            return true;

        if (Common.Contains(lower))
            return true;

        var core = lower.TrimEnd("0123456789!@#$%^&*()-_=+.?~ ".ToCharArray())
                        .TrimStart("0123456789!@#$%^&*()-_=+.?~ ".ToCharArray());

        if (core.Length > 0 && Common.Contains(core))
            return true;

        // One character over and over, or a straight run on the keyboard or the number line.
        if (lower.Distinct().Count() <= 3)
            return true;

        return Sequences.Any(run => lower.Contains(run, StringComparison.Ordinal) && run.Length * 2 >= lower.Length);
    }

    private static readonly string[] Sequences =
    [
        "0123456789", "1234567890", "9876543210", "abcdefghijklmnopqrstuvwxyz",
        "qwertyuiop", "asdfghjkl", "zxcvbnm", "qwertzuiop", "azertyuiop"
    ];

    /// <summary>
    /// The most-used passwords from public breach corpora, and the words people build them from.
    /// Deliberately short: it is a floor under the length rule, not a replacement for it.
    /// </summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "password", "passw0rd", "p@ssword", "p@ssw0rd", "pass", "passwd", "password1", "password12", "password123",
        "123456", "1234567", "12345678", "123456789", "1234567890", "12345", "1234", "111111", "000000", "123123",
        "654321", "666666", "121212", "112233", "123321", "987654321", "159753", "147258369",
        "qwerty", "qwerty123", "qwertyuiop", "azerty", "asdfgh", "asdfghjkl", "zxcvbnm", "1q2w3e", "1q2w3e4r",
        "1q2w3e4r5t", "qazwsx", "1qaz2wsx", "zaq12wsx", "q1w2e3r4",
        "abc123", "abcd1234", "abcdef", "abcdefg", "a1b2c3", "aa123456",
        "iloveyou", "letmein", "welcome", "welcome1", "admin", "admin123", "administrator", "root", "toor",
        "login", "master", "secret", "changeme", "default", "guest", "test", "test123", "testing", "demo",
        "monkey", "dragon", "football", "baseball", "soccer", "basketball", "superman", "batman", "shadow",
        "sunshine", "princess", "starwars", "whatever", "trustno1", "freedom", "hello", "hello123", "charlie",
        "michael", "jordan", "mustang", "access", "ninja", "flower", "summer", "winter", "spring", "autumn",
        "computer", "internet", "google", "microsoft", "samsung", "apple", "iphone", "android", "facebook",
        "egypt", "cairo", "alexandria", "mohamed", "mohammed", "ahmed", "ahmad", "mahmoud", "mostafa", "omar",
        "ali", "hassan", "hussein", "youssef", "mariam", "fatma", "sara", "nour", "yasmin", "salma",
        "allah", "bismillah", "alhamdulillah", "habibi", "zamalek", "ahly", "alahly", "masr",
        "school", "teacher", "student", "study", "science", "maths", "english", "arabic", "lesson", "exam",
        "content", "studio", "curriculum", "education", "question", "answer",
        "january", "february", "march", "april", "june", "july", "august", "september", "october",
        "november", "december", "monday", "friday", "sunday", "love", "lovely", "loveme", "family",
        "baby", "angel", "killer", "pokemon", "minecraft", "fortnite", "roblox", "gaming", "player"
    };
}
