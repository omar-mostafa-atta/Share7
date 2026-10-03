param(
    [string]$FtpHost = 'site92534.siteasp.net',
    [string]$Username = $env:SHARE7_FTP_USERNAME,
    [string]$Password = $env:SHARE7_FTP_PASSWORD,
    [string]$RemotePath = '/wwwroot'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Password)) { throw "Set SHARE7_FTP_PASSWORD or pass -Password." }
$root = $PSScriptRoot
$publishDir = Join-Path $root 'publish_output'

if (!(Test-Path $publishDir)) {
    Write-Error "Publish directory not found at $publishDir."
    exit 1
}

Write-Host '========================================================' -ForegroundColor Cyan
Write-Host ' Share7 Backend Deployment to MonsterASP.NET (FTP)' -ForegroundColor Cyan
Write-Host '========================================================' -ForegroundColor Cyan

$cleanHost = $FtpHost -replace '^ftp://', ''
$baseUri = 'ftp://' + $cleanHost
if (!$RemotePath.StartsWith('/')) { $RemotePath = '/' + $RemotePath }
if (!$RemotePath.EndsWith('/')) { $RemotePath = $RemotePath + '/' }

$credentials = New-Object System.Net.NetworkCredential($Username, $Password)

# Remove placeholder iisstart.htm if present
try {
    $delUri = $baseUri + $RemotePath + 'iisstart.htm'
    $delReq = [System.Net.FtpWebRequest]::Create($delUri)
    $delReq.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
    $delReq.Credentials = $credentials
    $delResp = $delReq.GetResponse()
    $delResp.Close()
    Write-Host '✓ Removed default iisstart.htm' -ForegroundColor Green
}
catch {
    # File might not exist
}

function Ensure-FtpDirectory($targetPath) {
    $uri = $baseUri + $targetPath
    $request = [System.Net.FtpWebRequest]::Create($uri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
    $request.Credentials = $credentials
    $request.UseBinary = $true
    $request.KeepAlive = $false
    try {
        $response = $request.GetResponse()
        $response.Close()
    }
    catch {
        # Directory might already exist
    }
}

function Upload-FtpFile($localFile, $remoteRelativePath) {
    $cleanRel = $remoteRelativePath -replace '\\', '/'
    $uri = $baseUri + $RemotePath + $cleanRel
    Write-Host "  -> Uploading $cleanRel..." -NoNewline

    $request = [System.Net.FtpWebRequest]::Create($uri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $request.Credentials = $credentials
    $request.UseBinary = $true
    $request.KeepAlive = $false

    $fileBytes = [System.IO.File]::ReadAllBytes($localFile)
    $request.ContentLength = $fileBytes.Length

    $stream = $request.GetRequestStream()
    $stream.Write($fileBytes, 0, $fileBytes.Length)
    $stream.Close()

    $response = $request.GetResponse()
    $response.Close()
    Write-Host ' Done!' -ForegroundColor Green
}

$items = Get-ChildItem -Path $publishDir -Recurse

# Create directories in hierarchical order
$dirs = $items | Where-Object { $_.PSIsContainer } | Sort-Object { $_.FullName.Length }
foreach ($d in $dirs) {
    $rel = $d.FullName.Substring($publishDir.Length).TrimStart('\', '/') -replace '\\', '/'
    Ensure-FtpDirectory ($RemotePath + $rel)
}

# Upload files
$files = $items | Where-Object { !$_.PSIsContainer }
Write-Host "`nUploading $($files.Count) files to $baseUri$RemotePath..." -ForegroundColor Yellow

$count = 0
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
foreach ($f in $files) {
    $rel = $f.FullName.Substring($publishDir.Length).TrimStart('\', '/') -replace '\\', '/'
    Upload-FtpFile $f.FullName $rel
    $count++
}
$stopwatch.Stop()

Write-Host '========================================================' -ForegroundColor Green
Write-Host " Deployment Complete! Successfully uploaded $count files in $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1))s." -ForegroundColor Green
Write-Host '========================================================' -ForegroundColor Green
