param(
    [string]$FtpUser = $env:SHARE7_FTP_USERNAME,
    [string]$FtpPass = $env:SHARE7_FTP_PASSWORD,
    [string]$FtpHost = 'site92534.siteasp.net',
    [string]$RemoteDir = '/wwwroot/',
    [string]$SiteUrl = 'https://shareh.runasp.net'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($FtpPass)) { throw "Set SHARE7_FTP_PASSWORD or pass -FtpPass." }
$root = $PSScriptRoot
$publishDir = Join-Path $root 'publish_output'

if (!(Test-Path $publishDir)) {
    Write-Error "Publish directory not found at $publishDir"
    exit 1
}

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Share7 MonsterASP.NET Deployment Engine" -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan

$userPass = "${FtpUser}:${FtpPass}"

# 1. Take app offline to release IIS process locks
Write-Host "`n[1/5] Taking remote application offline (app_offline.htm)..." -ForegroundColor Yellow
$offlineFile = Join-Path $root 'app_offline.htm'
"<!DOCTYPE html><html><head><title>Updating</title></head><body><h2>Share7 Backend Deploying...</h2></body></html>" | Out-File -FilePath $offlineFile -Encoding utf8
$offlineTarget = "ftp://${FtpHost}${RemoteDir}app_offline.htm"

& curl.exe -s -u $userPass --ftp-create-dirs -T $offlineFile $offlineTarget
Write-Host "[OK] App taken offline." -ForegroundColor Green
Start-Sleep -Seconds 3

# 2. Collect files to upload
$files = Get-ChildItem -Path $publishDir -Recurse -File
Write-Host "`n[2/5] Uploading $($files.Count) files to $FtpHost$RemoteDir..." -ForegroundColor Yellow

$total = $files.Count
$idx = 0
$failCount = 0
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($f in $files) {
    $idx++
    $rel = $f.FullName.Substring($publishDir.Length).TrimStart('\', '/') -replace '\\', '/'
    $targetUrl = "ftp://${FtpHost}${RemoteDir}${rel}"
    Write-Host "[$idx/$total] Uploading $rel..." -NoNewline

    & curl.exe -s -u $userPass --ftp-create-dirs -T $f.FullName $targetUrl
    $exitCode = $LASTEXITCODE

    if ($exitCode -eq 0) {
        Write-Host " OK" -ForegroundColor Green
    } else {
        Write-Host " FAILED (Exit $exitCode)" -ForegroundColor Red
        $failCount++
    }
}

$stopwatch.Stop()
Write-Host "`n[3/5] Files uploaded in $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1))s (Failures: $failCount)" -ForegroundColor Green

# 3. Bring application back online
Write-Host "`n[4/5] Bringing application online (deleting app_offline.htm and iisstart.htm)..." -ForegroundColor Yellow
$cred = New-Object System.Net.NetworkCredential($FtpUser, $FtpPass)

foreach ($fToDelete in @('app_offline.htm', 'iisstart.htm')) {
    try {
        $delUri = "ftp://${FtpHost}${RemoteDir}${fToDelete}"
        $delReq = [System.Net.FtpWebRequest]::Create($delUri)
        $delReq.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
        $delReq.Credentials = $cred
        $delResp = $delReq.GetResponse()
        $delResp.Close()
        Write-Host "[OK] Removed $fToDelete" -ForegroundColor Green
    } catch {
        # File might not exist
    }
}
Write-Host "[OK] App brought online." -ForegroundColor Green

# 4. Warm-up and smoke test live endpoint
Write-Host "`n[5/5] Testing live site health ($SiteUrl)..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

$healthSuccess = $false
for ($attempt = 1; $attempt -le 8; $attempt++) {
    try {
        Write-Host "  Attempt $attempt/8: GET $SiteUrl/swagger/v1/swagger.json..." -NoNewline
        $resp = Invoke-WebRequest -Uri "$SiteUrl/swagger/v1/swagger.json" -UseBasicParsing -TimeoutSec 30
        if ($resp.StatusCode -eq 200) {
            Write-Host " OK (HTTP 200)" -ForegroundColor Green
            $healthSuccess = $true
            break
        } else {
            Write-Host " HTTP $($resp.StatusCode)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host " Wait ($($_.Exception.Message))" -ForegroundColor Yellow
        Start-Sleep -Seconds 5
    }
}

if ($healthSuccess) {
    Write-Host "`n========================================================" -ForegroundColor Green
    Write-Host " DEPLOYMENT SUCCESSFUL!" -ForegroundColor Green
    Write-Host " Live Site: $SiteUrl" -ForegroundColor Green
    Write-Host " Swagger:   $SiteUrl/swagger" -ForegroundColor Green
    Write-Host " Studio:    $SiteUrl/studio" -ForegroundColor Green
    Write-Host "========================================================" -ForegroundColor Green
} else {
    Write-Host "`n========================================================" -ForegroundColor Yellow
    Write-Host " Files deployed, but initial warm-up response was not 200. Please check logs at $SiteUrl" -ForegroundColor Yellow
    Write-Host "========================================================" -ForegroundColor Yellow
}
