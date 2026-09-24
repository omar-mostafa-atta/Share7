using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace Share7.Tests.Contracts;

/// <summary>
/// Records a sequence of calls against the running API and compares them, byte for byte, with a
/// reviewed baseline file in <c>Contracts/Snapshots</c>.
/// <para>
/// <b>What is compared exactly:</b> routes, status codes, every property name, its type, its value
/// and its order; every id the fixture owns (shown by name — <c>lesson:solids</c>); version numbers;
/// text in both languages.
/// </para>
/// <para>
/// <b>What is normalised</b>, because it legitimately differs between runs: timestamps and dates;
/// access and refresh tokens; and ids the server mints while the test runs (a reward transaction,
/// an item), which become <c>&lt;id:1&gt;</c>, <c>&lt;id:2&gt;</c> by first appearance — so "the
/// same id appears in both places" is still checked even though its value is not.
/// </para>
/// <para>
/// <b>Recording a baseline.</b> Run with <c>SHARE7_UPDATE_SNAPSHOTS=1</c>. A missing baseline fails
/// the test otherwise, because a snapshot nobody reviewed protects nothing. On a mismatch the actual
/// output is written next to the baseline as <c>.received.json</c> for diffing.
/// </para>
/// </summary>
public sealed partial class ContractSnapshot
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,

        // Arabic stays readable in the baseline instead of becoming \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IReadOnlyDictionary<Guid, string> _labels;
    private readonly Dictionary<Guid, string> _minted = [];
    private readonly JsonArray _calls = [];

    public ContractSnapshot(ContractData data) => _labels = data.Labels;

    /// <summary>Makes one call, records it, and returns the body as the server sent it.</summary>
    public async Task<JsonNode?> CallAsync(HttpClient client, HttpMethod method, string path, object? body = null, string caller = "anonymous")
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        JsonNode? parsed = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { parsed = JsonNode.Parse(text); }
            catch (JsonException) { parsed = JsonValue.Create(text); }
        }

        _calls.Add(new JsonObject
        {
            ["request"] = $"{method.Method} {NormalisePath(path)}",
            ["as"] = caller,
            ["requestBody"] = body is null ? null : Normalise(JsonSerializer.SerializeToNode(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            ["status"] = (int)response.StatusCode,
            ["body"] = Normalise(parsed?.DeepClone())
        });

        return parsed;
    }

    public Task<JsonNode?> GetAsync(HttpClient client, string path, string caller) =>
        CallAsync(client, HttpMethod.Get, path, null, caller);

    public Task<JsonNode?> PostAsync(HttpClient client, string path, object body, string caller) =>
        CallAsync(client, HttpMethod.Post, path, body, caller);

    /// <summary>Compares everything recorded so far with <c>Snapshots/{name}.json</c>.</summary>
    public void Verify(string name, [CallerFilePath] string callerFile = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
        var baselinePath = Path.Combine(directory, name + ".json");
        var receivedPath = Path.Combine(directory, name + ".received.json");

        var actual = JsonSerializer.Serialize(_calls, Pretty).ReplaceLineEndings("\n") + "\n";

        if (Environment.GetEnvironmentVariable("SHARE7_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(baselinePath, actual, new UTF8Encoding(false));
            if (File.Exists(receivedPath)) File.Delete(receivedPath);
            return;
        }

        if (!File.Exists(baselinePath))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(receivedPath, actual, new UTF8Encoding(false));
            Assert.Fail($"No baseline for '{name}'. Review {receivedPath}, then record it with SHARE7_UPDATE_SNAPSHOTS=1.");
        }

        var expected = File.ReadAllText(baselinePath).ReplaceLineEndings("\n");
        if (expected == actual)
        {
            if (File.Exists(receivedPath)) File.Delete(receivedPath);
            return;
        }

        File.WriteAllText(receivedPath, actual, new UTF8Encoding(false));
        Assert.Fail(
            $"The game-facing contract changed in '{name}'. The Unity client depends on this output " +
            $"exactly.\n{FirstDifference(expected, actual)}\nCompare {baselinePath}\n   with {receivedPath}\n" +
            "If the change is deliberate and the client team has agreed to it, re-record with SHARE7_UPDATE_SNAPSHOTS=1.");
    }

    // ------------------------------------------------------------- normalising

    private string NormalisePath(string path) =>
        GuidPattern().Replace(path, match => Label(Guid.Parse(match.Value)));

    private JsonNode? Normalise(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                // A token claim read back from /api/auth/me: its time claims change every run.
                var isTimeClaim = obj["type"] is JsonValue type
                    && type.TryGetValue<string>(out var claimType)
                    && TimeClaims.Contains(claimType);

                foreach (var (key, value) in obj.ToList())
                {
                    obj[key] = value is JsonValue && key.EndsWith("token", StringComparison.OrdinalIgnoreCase) ? "<token>"
                        : value is JsonValue && key == "traceId" ? "<trace-id>"
                        : isTimeClaim && key == "value" ? "<epoch-seconds>"
                        : Normalise(value?.DeepClone());
                }
                return obj;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    array[i] = Normalise(array[i]?.DeepClone());
                return array;

            case JsonValue value when value.TryGetValue<string>(out var text):
                if (Guid.TryParse(text, out var id)) return Label(id);
                if (TimestampPattern().IsMatch(text)) return "<timestamp>";
                if (DatePattern().IsMatch(text)) return "<date>";
                return value;

            default:
                return node;
        }
    }

    /// <summary>JWT claims holding seconds since the epoch.</summary>
    private static readonly HashSet<string> TimeClaims = ["exp", "iat", "nbf", "auth_time"];

    private string Label(Guid id)
    {
        if (id == Guid.Empty) return "<empty-id>";
        if (_labels.TryGetValue(id, out var label)) return label;
        if (!_minted.TryGetValue(id, out var minted))
            _minted[id] = minted = $"<id:{_minted.Count + 1}>";
        return minted;
    }

    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        for (var i = 0; i < Math.Max(e.Length, a.Length); i++)
        {
            var left = i < e.Length ? e[i] : "<end>";
            var right = i < a.Length ? a[i] : "<end>";
            if (left != right)
                return $"First difference at line {i + 1}:\n  expected: {left.Trim()}\n  actual:   {right.Trim()}";
        }
        return "The files differ only in whitespace.";
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}")]
    private static partial Regex TimestampPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DatePattern();
}
