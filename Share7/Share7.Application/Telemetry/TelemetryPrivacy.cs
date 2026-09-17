using System.Text.RegularExpressions;

namespace Share7.Application.Telemetry;

/// <summary>
/// The last gate before a parameter becomes a row that lives for ninety days.
/// <para>
/// **Share7's users are children, and this is the enforcement point the event vocabulary's
/// authoring rule leans on.** <c>CommerceAnalyticsEvents</c> already says no event may carry a
/// username, email, phone number, token or free text a child typed — but a rule enforced only by
/// review is a rule that holds until somebody is in a hurry. This is the same rule, checked by the
/// server, against every batch, from every build that will ever exist.
/// </para>
/// <para>
/// **It refuses the event rather than stripping the field.** A stripped field is a gap discovered
/// months later by whoever tries to answer a question with it, and by then the build that produced
/// it has shipped to a million devices. A refusal comes back with
/// <c>TelemetryRejectReasons.ForbiddenParam</c>, shows up in the console the same day, and is
/// fixable before the release goes wide.
/// </para>
/// </summary>
public static class TelemetryPrivacy
{
    /// <summary>
    /// Parameter keys that are refused outright.
    /// <para>
    /// Matched as whole words against the key split on <c>_</c>, so <c>user_email</c> and
    /// <c>email</c> both fail while <c>email_verified_count</c> — a count, not an address — does
    /// not. Substring matching was the first attempt and it refused <c>name</c> inside
    /// <c>tournament</c>.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ForbiddenTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // Identity. The server already knows who this is — see Rule 1.
        "email", "mail", "username", "user", "userid", "uid", "account", "accountid",
        "phone", "msisdn", "mobile", "name", "firstname", "lastname", "fullname",
        "dob", "birthday", "birthdate", "age", "gender", "address", "postcode", "zip",

        // Credentials. Nothing in an analytics payload has any business carrying one.
        "token", "accesstoken", "refreshtoken", "password", "secret", "auth", "authorization",
        "jwt", "apikey", "credential", "session_token",

        // Device identity, as opposed to device class. `device_model` is fine and is a column;
        // anything that identifies the handset itself is a persistent identifier under COPPA.
        "idfa", "idfv", "gaid", "adid", "advertisingid", "androidid", "imei", "macaddress",
        "deviceid", "installid", "fingerprint", "ip", "ipaddress", "lat", "lon", "latitude",
        "longitude", "geo",

        // Free text. There is no safe way to store something a child typed.
        "text", "message", "comment", "note", "input", "query", "search", "chat", "answer_text"
    };

    /// <summary>
    /// Wallet balances are refused for a reason that is not privacy at all.
    /// <para>
    /// It is the one already written on <c>CommerceAnalyticsEvents</c>: a balance in this stream
    /// becomes a second record of what a child owns, kept next to the real one, derived from a
    /// client that is explicitly not authoritative about it — and somebody eventually reconciles
    /// against it. Prices and amounts are fine, because they describe the transaction rather than
    /// the account. Separate set so the refusal message can say which rule it broke.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ForbiddenBalanceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "balance", "balances", "wallet", "totalcoins", "coinbalance", "xptotal", "networth"
    };

    /// <summary>
    /// <c>snake_case</c>, starting with a letter. Enforced because the name is a wire format that
    /// outlives every dashboard built on it, and a vocabulary where <c>LessonDone</c> and
    /// <c>lesson_done</c> both exist is one nobody can query confidently.
    /// </summary>
    private static readonly Regex NamePattern =
        new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidEventName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= Domain.Telemetry.TelemetryNames.MaxNameLength &&
        NamePattern.IsMatch(name);

    /// <summary>
    /// Whether a parameter key may be stored.
    /// </summary>
    /// <param name="key">The parameter key, as the client wrote it.</param>
    /// <param name="reason">Which rule it broke, for the rejection the client receives.</param>
    public static bool IsAllowedParameter(string? key, out string? reason)
    {
        reason = null;

        if (string.IsNullOrWhiteSpace(key))
        {
            reason = "blank parameter key";
            return false;
        }

        if (!NamePattern.IsMatch(key))
        {
            reason = $"parameter key '{key}' is not snake_case";
            return false;
        }

        // Split rather than substring: `tournament` contains `name` and is perfectly fine, while
        // `user_name` is not. The token boundary is what tells them apart.
        foreach (var token in key.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            if (ForbiddenTokens.Contains(token))
            {
                reason = $"parameter '{key}' looks like a personal identifier";
                return false;
            }

            if (ForbiddenBalanceTokens.Contains(token))
            {
                reason = $"parameter '{key}' looks like a wallet balance; the ledger owns those";
                return false;
            }
        }

        // The whole key, for the compound names that carry no underscore.
        if (ForbiddenTokens.Contains(key) || ForbiddenBalanceTokens.Contains(key))
        {
            reason = $"parameter '{key}' is on the denylist";
            return false;
        }

        return true;
    }
}
