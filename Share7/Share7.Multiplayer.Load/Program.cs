using System.Text.Json;
using Share7.Multiplayer.Load;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("Usage: --base-url URL --game-id GUID [--hosts 1] [--seconds 60] [--poll-seconds 1] [--mode-key KEY] [--protocol 1] [--output report.json] [--allow-remote]");
    Console.WriteLine("Dedicated test accounts come from SHARE7_LOAD_ACCOUNTS_JSON: an array of userId/accessToken objects. No credentials are printed or saved.");
    return 0;
}

try
{
    var values = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--allow-remote") continue;
        if (!args[i].StartsWith("--") || i + 1 >= args.Length) throw new ArgumentException("Each option needs a value.");
        values.Add(args[i], args[++i]);
    }
    string Required(string key) => values.GetValueOrDefault(key) ?? throw new ArgumentException($"Missing {key}.");
    int Number(string key, int fallback) => values.TryGetValue(key, out var value) ? int.Parse(value) : fallback;
    var url = new Uri(Required("--base-url"));
    if (url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
        throw new ArgumentException("Use a plain HTTP(S) base URL without credentials, a query or a fragment.");
    if (!url.IsLoopback && !args.Contains("--allow-remote")) throw new ArgumentException("A remote staging run requires --allow-remote.");
    var accounts = JsonSerializer.Deserialize<LoadAccount[]>(Environment.GetEnvironmentVariable("SHARE7_LOAD_ACCOUNTS_JSON") ?? "[]",
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    var settings = new LoadSettings(Guid.Parse(Required("--game-id")), Number("--hosts", 1), Number("--seconds", 60),
        Number("--poll-seconds", 1), Number("--protocol", 1), values.GetValueOrDefault("--mode-key"));
    settings.Validate(accounts);
    using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = accounts.Length, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
    using var http = new HttpClient(handler) { BaseAddress = url, Timeout = TimeSpan.FromSeconds(30) };
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    var report = await new LoadRunner(http).RunAsync(settings, accounts, stop.Token);
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (values.TryGetValue("--output", out var output)) await File.WriteAllTextAsync(output, json);
    return report.Routes.Any(r => r.Failures > 0) ? 1 : 0;
}
catch (Exception e) when (e is ArgumentException or FormatException or JsonException or InvalidOperationException)
{
    // Parsing failures can echo the offending value; never echo account input or token contents.
    Console.Error.WriteLine($"Cannot run: {e.GetType().Name}. Check options and the dedicated account environment variable.");
    return 2;
}
