# Multiplayer HTTP load harness

Dependency-free .NET 8 driver for the existing session registry. Creates public lobbies, confirms
them, matchmakes guests, polls recovery and session state, heartbeats hosts at the **server's**
cadence, then leaves guests and closes hosts. It never submits scores or grants rewards. Photon
traffic is outside this harness.

Use dedicated disposable staging accounts with valid access tokens. Supply an array of
`userId`/`accessToken` objects through `SHARE7_LOAD_ACCOUNTS_JSON`, using your secret manager or local
shell environment. Account input and response bodies are never printed or written to the report.
Do not put tokens in arguments, tracked files, screenshots or reports.

```powershell
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
dotnet run --project Share7.Multiplayer.Load -- --base-url http://127.0.0.1:5080 --game-id <dedicated-game-guid> --hosts 10 --seconds 120 --output load-report.json
```

Run from `Share7/`. `--mode-key`, `--protocol` and `--poll-seconds` are optional. A non-loopback URL
requires `--allow-remote`; use a dedicated staging deployment approved for load, with enough test
accounts. The internal shared testing backend is **not** a default load target. The game/mode must
support multiplayer, have an appropriate seat cap, and be available to all test accounts. Extra
guests create lobbies if the initial ones fill.

Ctrl+C stops traffic and runs cleanup with a separate 30-second deadline. Cleanup affects only rooms
and seats created or joined by this run; a failed cleanup appears in the report and may require
ordinary session expiry. Supply fresh accounts rather than accounts with existing live seats.

The report contains only account/host **counts**, elapsed seconds, request throughput, and per-route
request/error counts, HTTP status counts, sample size, P50/P95/P99 and maximum latency. Each route
keeps a bounded uniform reservoir of 8,192 observations: beyond that, percentiles are estimates.
Status 0 denotes a transport failure, timeout or deliberate cancellation; the failure count excludes
cancellation at the run deadline. Cleanup and startup are included in elapsed time and route counts.
Exit 0 means no request failures; 1 means failures were recorded; 2 means invalid configuration.

Run increasing account counts and hold a stable configuration between runs. Record API process CPU,
memory, SQL CPU/batch requests per second and the `Share7.Multiplayer` meter alongside this report
using the deployment's own monitoring. HTTP percentiles alone do not establish server capacity.
Keep 1K–1M concurrency projections in `MultiplayerPlatform.md` labelled estimates until actual
deployment measurements support them.

`MultiplayerLoadContractTests` executes this driver against the real local API and temporary SQL
database, verifies route coverage and cleanup, and emits a sanitized report into the test output.
`MultiplayerLoadTests` separately sends 100 SQL-backed concurrent joins at the last room seat.
