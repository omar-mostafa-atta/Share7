param(
    [string]$FtpUser = $env:SHARE7_FTP_USERNAME,
    [string]$FtpPass = $env:SHARE7_FTP_PASSWORD,
    [string]$FtpHost = 'site92534.siteasp.net',
    [string]$RemoteDir = '/wwwroot/'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($FtpPass)) { throw "Set SHARE7_FTP_PASSWORD or pass -FtpPass." }
$root = $PSScriptRoot
$publishDir = Join-Path $root 'publish_output'

Write-Host '========================================================' -ForegroundColor Cyan
Write-Host ' Fast FTP Upload via cURL to MonsterASP.NET' -ForegroundColor Cyan
Write-Host '========================================================' -ForegroundColor Cyan

$files = Get-ChildItem -Path $publishDir -Recurse -File
Write-Host "Found $($files.Count) files to upload." -ForegroundColor Yellow

$total = $files.Count
$idx = 0
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($f in $files) {
    $idx++
    $rel = $f.FullName.Substring($publishDir.Length).TrimStart('\', '/') -replace '\\', '/'
    $targetUrl = "ftp://$FtpHost$RemoteDir$rel"
    Write-Host "[$idx/$total] Uploading $rel..." -NoNewline

    $p = Start-Process -FilePath "curl.exe" -ArgumentList @(
        "-s",
        "-u", "$FtpUser`:$FtpPass",
        "--ftp-create-dirs",
        "-T", "`"$($f.FullName)`"",
        "$targetUrl"
    ) -NoNewWindow -PassThru -Wait

    if ($p.ExitCode -eq 0) {
        Write-Host " OK" -ForegroundColor Green
    } else {
        Write-Host " FAILED (Exit $($p.ExitCode))" -ForegroundColor Red
    }
}

$stopwatch.Stop()
Write-Host '========================================================' -ForegroundColor Green
Write-Host " Upload Complete in $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1))s!" -ForegroundColor Green
Write-Host '========================================================' -ForegroundColor Green
