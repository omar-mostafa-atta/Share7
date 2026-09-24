using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Share7.Tests.Infrastructure;
using Xunit;

namespace Share7.Tests.Contracts;

/// <summary>
/// The real API, running as its own process against its own throwaway database, called over HTTP
/// exactly as the Unity client calls it.
/// <para>
/// <b>Why a process and not an in-memory test server.</b> What these tests guard is the wire — the
/// routes, the JSON the serializer actually writes, the status codes the auth middleware actually
/// returns. A real Kestrel host on a loopback port exercises all of that and needs nothing the
/// solution does not already build. (It also avoids adding a test-server package this machine does
/// not have.)
/// </para>
/// <para>
/// The database is migrated by the fixture, the host then starts on it — which runs the same
/// startup path production runs: migrations (a no-op by then), roles, the telemetry vocabulary, the
/// seed admin — and <see cref="ContractData"/> writes the fixed curriculum the snapshots describe.
/// </para>
/// </summary>
public class ContractHost : IAsyncLifetime
{
    /// <summary>Which tables the API answers the game from — see <c>GameContractModes.cs</c>.</summary>
    protected virtual string ReadModel => "Legacy";

    private readonly SqlServerFixture _database = new();
    private readonly StringBuilder _output = new();
    private Process? _process;

    public HttpClient Http { get; private set; } = null!;

    public ContractData Data { get; private set; } = null!;

    public string ConnectionString => _database.ConnectionString;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        var port = FreePort();
        var apiDll = LocateApi();

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(apiDll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(apiDll);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");

        // Its own environment name, so neither Development's seeding nor Production's guards apply.
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Contract";
        start.Environment["DOTNET_ENVIRONMENT"] = "Contract";
        start.Environment["ConnectionStrings__DefaultConnection"] = _database.ConnectionString;
        start.Environment["JwtSettings__Secret"] = new string('c', 64);
        start.Environment["ContentSeed__Enabled"] = "false";
        start.Environment["SeedAdmin__Password"] = ContractHostSecrets.AdminPassword;
        start.Environment["RateLimiting__Enabled"] = "false";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Curriculum__ReadModel"] = ReadModel;
        start.Environment["Curriculum__ShadowSampleRate"] = "1";

        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the API process.");
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (_output) _output.AppendLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_output) _output.AppendLine(e.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(60) };

        await WaitUntilReadyAsync();

        await using var context = _database.CreateContext();
        Data = await ContractData.WriteAsync(context);
    }

    public async Task DisposeAsync()
    {
        Http?.Dispose();

        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process?.Dispose();
        await _database.DisposeAsync();
    }

    /// <summary>A client signed in as one of the fixture's students.</summary>
    public async Task<HttpClient> SignedInAsync(ContractStudent student)
    {
        var login = await Http.PostAsJsonAsync("/api/auth/login", new { username = student.Username, password = ContractData.Password });
        var body = await login.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();

        var token = body?["accessToken"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"Could not sign in {student.Username}: {login.StatusCode} {body}");

        var client = new HttpClient { BaseAddress = Http.BaseAddress, Timeout = Http.Timeout };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>What the API printed, for a failure message.</summary>
    public string Output
    {
        get { lock (_output) return _output.ToString(); }
    }

    private async Task WaitUntilReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
                throw new InvalidOperationException($"The API exited during startup (code {_process.ExitCode}):\n{Output}");

            try
            {
                var response = await Http.GetAsync("/api/time");
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"The API did not become ready within 90 seconds:\n{Output}");
    }

    /// <summary>
    /// The API's build output, next to this test assembly's: same configuration, same target.
    /// The test project references the API project (without its assembly) purely so it is built first.
    /// </summary>
    private static string LocateApi()
    {
        var overridePath = Environment.GetEnvironmentVariable("SHARE7_API_DLL");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        // …/Share7.Tests/bin/{Configuration}/{tfm}/
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var tfm = testOutput.Name;
        var configuration = testOutput.Parent!.Name;
        var solution = testOutput.Parent.Parent!.Parent!.Parent!.FullName;

        var dll = Path.Combine(solution, "Share7", "bin", configuration, tfm, "Share7.API.dll");
        if (!File.Exists(dll))
            throw new FileNotFoundException($"Build the API first — expected it at {dll}.", dll);

        return dll;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[CollectionDefinition(Name)]
public class ContractCollection : ICollectionFixture<ContractHost>
{
    public const string Name = "game-contract";
}

/// <summary>The seed admin the contract host starts with — used by the shadow checks to read the tally.</summary>
public static class ContractHostSecrets
{
    public const string AdminPassword = "Contract#Admin-2026";
}
