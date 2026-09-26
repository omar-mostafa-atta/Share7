# Starts the API, the Admin Console and the Content Studio together, each in its own window.
#
#   .\dev.ps1           dev loop  — Vite on :5173 (console) and :5174/studio (Studio), both
#                                   proxying /api to the API on :7147
#   .\dev.ps1 -Built    prod path — builds the console into wwwroot, API serves it on :7147;
#                                   the Studio still runs on :5174/studio
#
# The content team signs in at one fixed address: http://localhost:5174/studio here, and /studio on
# the published site (Share7/Hosting/StudioHosting.cs). Both ports are fixed (strictPort), so the
# console can never take the Studio's port. The Admin Console shows that address, with the username
# and password, whenever a member is created.
#
# The API must run the `https` profile either way: vite.config.ts proxies to
# https://localhost:7147, so the `http`-only profile (:5215) leaves every call unproxied.

param(
    [switch]$Built
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$api = Join-Path $root 'Share7\Share7.API.csproj'
$web = Join-Path $root 'Share7.Web'
$studio = Join-Path $root 'Share7.Studio'

# A dev server left over from a previous run holds its port and silently serves stale code, so
# whatever owns each port is stopped before a new one starts.
foreach ($port in 5173, 5174) {
    $busy = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    foreach ($c in $busy) {
        Write-Host "Stopping process $($c.OwningProcess) already listening on :$port" -ForegroundColor Yellow
        try { Stop-Process -Id $c.OwningProcess -Force -ErrorAction Stop } catch {}
    }
}

function Start-Studio {
    Write-Host 'Starting the Content Studio dev server on :5174 in a new window...' -ForegroundColor Cyan
    Start-Process powershell -ArgumentList @(
        '-ExecutionPolicy', 'Bypass',
        '-NoExit', '-Command',
        "Write-Host 'Share7.Studio (dev)' -ForegroundColor Magenta; Set-Location '$studio'; npm.cmd run dev"
    )
}

if ($Built) {
    Write-Host 'Building the SPA into wwwroot...' -ForegroundColor Cyan
    Push-Location $web
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "SPA build failed with exit code $LASTEXITCODE" }
    }
    finally { Pop-Location }

    Start-Studio

    Write-Host ''
    Write-Host 'Starting the API. It serves the console; the Studio is on its own window:' -ForegroundColor Green
    Write-Host '  https://localhost:7147/   Admin Console' -ForegroundColor Green
    Write-Host '  http://localhost:5174/studio   Content Studio' -ForegroundColor Green
    Write-Host ''
    $env:DOTNET_ROLL_FORWARD = 'LatestMajor'
    dotnet run --project $api --launch-profile https
    return
}

Write-Host 'Starting the API on :7147 in a new window...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-ExecutionPolicy', 'Bypass',
    '-NoExit', '-Command',
    "`$env:DOTNET_ROLL_FORWARD = 'LatestMajor'; Write-Host 'Share7 API' -ForegroundColor Cyan; dotnet run --project '$api' --launch-profile https"
)

Write-Host 'Starting the Vite dev server on :5173 in a new window...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-ExecutionPolicy', 'Bypass',
    '-NoExit', '-Command',
    "Write-Host 'Share7.Web (dev)' -ForegroundColor Magenta; Set-Location '$web'; npm.cmd run dev"
)

Start-Studio

Write-Host ''
Write-Host 'Open these once the windows have settled:' -ForegroundColor Green
Write-Host '  http://localhost:5173/   Admin Console' -ForegroundColor Green
Write-Host '  http://localhost:5174/studio   Content Studio (give this to the content team)' -ForegroundColor Green
Write-Host ''
Write-Host 'Edits to Share7.Web reload instantly. Edits to C# need the API window restarted.'
