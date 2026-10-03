using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Share7.Multiplayer.Load;

public sealed record LoadAccount(Guid UserId, string AccessToken);
public sealed record LoadSettings(Guid GameId, int Hosts, int Seconds, int PollSeconds, int Protocol, string? ModeKey = null)
{
    public void Validate(IReadOnlyList<LoadAccount> accounts)
    {
        if (GameId == Guid.Empty || Hosts < 1 || Hosts >= accounts.Count || Seconds is < 1 or > 86400
            || PollSeconds is < 1 or > 60 || Protocol < 1 || accounts.Count > 10000
            || accounts.Any(a => a.UserId == Guid.Empty || string.IsNullOrWhiteSpace(a.AccessToken))
            || accounts.Select(a => a.UserId).Distinct().Count() != accounts.Count)
            throw new ArgumentException("Provide distinct test accounts, at least one host and guest, and bounded positive settings.");
    }
}

public sealed record RouteReport(string Route, long Requests, long Failures, IReadOnlyDictionary<int, long> StatusCodes,
    int Samples, double P50Ms, double P95Ms, double P99Ms, double MaxMs);
public sealed record LoadReport(int Accounts, int Hosts, double ElapsedSeconds, double RequestsPerSecond, IReadOnlyList<RouteReport> Routes);

/// <summary>HTTP registry traffic only: no simulated Photon traffic, results, rewards or player analytics.</summary>
public sealed class LoadRunner(HttpClient http)
{
    private readonly ConcurrentDictionary<string, Measurements> _measurements = new();
    private readonly ConcurrentDictionary<Guid, (LoadAccount Account, bool Host)> _seated = new();

    public async Task<LoadReport> RunAsync(LoadSettings settings, IReadOnlyList<LoadAccount> accounts, CancellationToken cancellationToken = default)
    {
        settings.Validate(accounts);
        _measurements.Clear(); _seated.Clear(); _memberships.Clear();
        var watch = Stopwatch.StartNew();
        using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        duration.CancelAfter(TimeSpan.FromSeconds(settings.Seconds));
        try
        {
            // Confirm all initial lobbies before guests start searching.
            await Task.WhenAll(accounts.Take(settings.Hosts).Select(a => OpenAsync(a, settings, duration.Token)));
            await Task.WhenAll(accounts.Select((a, i) => TrafficAsync(a, i < settings.Hosts, settings, duration.Token)));
        }
        catch (OperationCanceledException) when (duration.IsCancellationRequested) { }
        finally
        {
            // Cleanup has its own bounded time budget, including when Ctrl+C interrupted the run.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            foreach (var hosts in new[] { false, true })
                await Task.WhenAll(_memberships.Values.Where(m => m.Host == hosts).Select(m => SendAsync(m.Account, HttpMethod.Post,
                    $"/api/multiplayer/sessions/{m.Session}/{(hosts ? "close" : "leave")}", hosts ? "close" : "leave",
                    new { requestId = Guid.NewGuid().ToString() }, cleanup.Token, countCancellation: true)));
        }
        watch.Stop();
        var routes = _measurements.OrderBy(m => m.Key).Select(m => m.Value.Report(m.Key)).ToArray();
        return new(accounts.Count, settings.Hosts, watch.Elapsed.TotalSeconds,
            routes.Sum(r => r.Requests) / Math.Max(.001, watch.Elapsed.TotalSeconds), routes);
    }

    private async Task OpenAsync(LoadAccount account, LoadSettings settings, CancellationToken token)
    {
        var room = await SendAsync(account, HttpMethod.Post, "/api/multiplayer/sessions", "create", new
        { gameId = settings.GameId, modeKey = settings.ModeKey, protocolVersion = settings.Protocol,
          transportSessionName = $"load_{Guid.NewGuid():N}", requestId = Guid.NewGuid().ToString() }, token);
        if (room is null) return;
        var id = room.Value.GetProperty("id").GetGuid();
        _seated[id] = (account, true);
        _memberships[account.UserId] = (id, true, account);
        await SendAsync(account, HttpMethod.Post, $"/api/multiplayer/sessions/{id}/start", "confirm",
            new { requestId = Guid.NewGuid().ToString() }, token);
    }

    private async Task TrafficAsync(LoadAccount account, bool initialHost, LoadSettings settings, CancellationToken token)
    {
        Guid? sessionId = _seated.FirstOrDefault(s => s.Value.Account.UserId == account.UserId).Key;
        if (sessionId == Guid.Empty) sessionId = null;
        if (!initialHost)
        {
            var matched = await SendAsync(account, HttpMethod.Post, "/api/multiplayer/matchmaking", "matchmaking", new
            { gameId = settings.GameId, modeKey = settings.ModeKey, protocolVersion = settings.Protocol, createIfNoneFound = true,
              transportSessionName = $"load_{Guid.NewGuid():N}", requestId = Guid.NewGuid().ToString() }, token);
            if (matched is { } found && found.TryGetProperty("session", out var room) && room.ValueKind == JsonValueKind.Object)
            {
                sessionId = room.GetProperty("id").GetGuid();
                var isHost = room.GetProperty("hostUserId").GetGuid() == account.UserId;
                // Store each user's membership: multiple guests in one session must all leave.
                _memberships[account.UserId] = (sessionId.Value, isHost, account);
                if (isHost)
                {
                    _seated[sessionId.Value] = (account, true);
                    await SendAsync(account, HttpMethod.Post, $"/api/multiplayer/sessions/{sessionId}/start", "confirm",
                        new { requestId = Guid.NewGuid().ToString() }, token);
                }
            }
        }
        else if (sessionId is { } hosted) _memberships[account.UserId] = (hosted, true, account);
        var nextHeartbeat = DateTime.MinValue;
        while (!token.IsCancellationRequested)
        {
            await SendAsync(account, HttpMethod.Get, "/api/multiplayer/sessions", "recover", null, token);
            if (sessionId is { } id)
            {
                var room = await SendAsync(account, HttpMethod.Get, $"/api/multiplayer/sessions/{id}", "get", null, token);
                if (room is { } state && state.GetProperty("hostUserId").GetGuid() == account.UserId && DateTime.UtcNow >= nextHeartbeat)
                {
                    var roster = state.GetProperty("players").EnumerateArray()
                        .Where(p => p.GetProperty("status").GetString() is not ("Left" or "Removed"))
                        .Select(p => p.GetProperty("userId").GetGuid()).ToArray();
                    var beat = await SendAsync(account, HttpMethod.Post, $"/api/multiplayer/sessions/{id}/heartbeat", "heartbeat",
                        new { connectedUserIds = roster }, token);
                    nextHeartbeat = DateTime.UtcNow.AddSeconds(beat?.GetProperty("nextHeartbeatInSeconds").GetInt32() ?? 15);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), token);
        }
    }

    private readonly ConcurrentDictionary<Guid, (Guid Session, bool Host, LoadAccount Account)> _memberships = new();

    private async Task<JsonElement?> SendAsync(LoadAccount account, HttpMethod method, string path, string route, object? body,
        CancellationToken token, bool countCancellation = false)
    {
        var watch = Stopwatch.StartNew(); var status = 0; var failed = true;
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request, token);
            status = (int)response.StatusCode; failed = !response.IsSuccessStatusCode;
            if (failed) return null;
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            return document.RootElement.Clone();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { failed = countCancellation; return null; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { failed = true; return null; }
        finally { _measurements.GetOrAdd(route, _ => new()).Record(watch.Elapsed.TotalMilliseconds, status, failed); }
    }

    private sealed class Measurements
    {
        private const int Capacity = 8192;
        private readonly List<double> _samples = [];
        private readonly Dictionary<int, long> _statuses = [];
        private long _count, _failed; private double _max;
        public void Record(double milliseconds, int status, bool failed)
        {
            lock (_samples)
            {
                _count++; if (failed) _failed++;
                _statuses[status] = _statuses.GetValueOrDefault(status) + 1; _max = Math.Max(_max, milliseconds);
                if (_samples.Count < Capacity) _samples.Add(milliseconds);
                else { var slot = Random.Shared.NextInt64(_count); if (slot < Capacity) _samples[(int)slot] = milliseconds; }
            }
        }
        public RouteReport Report(string route)
        {
            lock (_samples)
            {
                var sorted = _samples.Order().ToArray();
                double At(double p) => sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];
                return new(route, _count, _failed, new Dictionary<int, long>(_statuses), sorted.Length, At(.5), At(.95), At(.99), _max);
            }
        }
    }
}
