# Starts the API and the React dev server together, each in its own window.
#
#   .\dev.ps1           dev loop  — Vite on :5173 proxying /api to the API on :7147
#   .\dev.ps1 -Built    prod path — builds the SPA into wwwroot/app, API serves it on :7147
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

# A dev server left over from a previous run holds :5173 and silently serves stale code, so
# whatever owns the port is stopped before a new one starts.
$busy = Get-NetTCPConnection -LocalPort 5173 -State Listen -ErrorAction SilentlyContinue
foreach ($c in $busy) {
    Write-Host "Stopping process $($c.OwningProcess) already listening on :5173" -ForegroundColor Yellow
    try { Stop-Process -Id $c.OwningProcess -Force -ErrorAction Stop } catch {}
}

if ($Built) {
    Write-Host 'Building the SPA into wwwroot/app...' -ForegroundColor Cyan
    Push-Location $web
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "SPA build failed with exit code $LASTEXITCODE" }
    }
    finally { Pop-Location }

    Write-Host ''
    Write-Host 'Starting the API. It serves both consoles:' -ForegroundColor Green
    Write-Host '  React console : https://localhost:7147/app/' -ForegroundColor Green
    Write-Host '  Old console   : https://localhost:7147/' -ForegroundColor Green
    Write-Host ''
    dotnet run --project $api --launch-profile https
    return
}

Write-Host 'Starting the API on :7147 in a new window...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Write-Host 'Share7 API' -ForegroundColor Cyan; dotnet run --project '$api' --launch-profile https"
)

Write-Host 'Starting the Vite dev server on :5173 in a new window...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "Write-Host 'Share7.Web (dev)' -ForegroundColor Magenta; Set-Location '$web'; npm run dev"
)

Write-Host ''
Write-Host 'Open this once both windows have settled:' -ForegroundColor Green
Write-Host '  http://localhost:5173/app/' -ForegroundColor Green
Write-Host ''
Write-Host 'Edits to Share7.Web reload instantly. Edits to C# need the API window restarted.'
