# Rehearses a backup-and-restore of a Share7 database, and proves the restored copy matches.
#
#   .\ops\rehearse-restore.ps1 -Database Shareh
#   .\ops\rehearse-restore.ps1 -Server "prod-sql\SHARE7" -Database Shareh -BackupDirectory "D:\Backups"
#
# What it does, in order:
#   1. Fingerprints the source: which migrations are applied, and how many rows every table holds.
#   2. Takes a COPY_ONLY backup with a checksum. COPY_ONLY leaves any existing backup chain exactly as
#      it was, so running this against production does not disturb the real backup schedule. The
#      source database is only read.
#   3. Verifies the backup file (RESTORE VERIFYONLY ... WITH CHECKSUM).
#   4. Restores it under a NEW name - never over the source - and fingerprints that.
#   5. Compares the two fingerprints table by table, then drops the restored copy (unless -KeepRestored).
#
# Run it before Phase 2 of the engine rebuild starts, and again before each cutover: "we have
# backups" means nothing until a restore has been seen to produce the same data.
#
# Needs only Windows PowerShell 5.1 (System.Data.SqlClient is built in) and an account that may
# back up the source and create a database on the server. Integrated security by default; pass
# -Credential for SQL authentication.

param(
    [string]$Server = '.\SQLEXPRESS',
    [Parameter(Mandatory = $true)][string]$Database,

    # Where the .bak goes. Defaults to the instance's own backup folder, which the SQL Server
    # service account can always write to (it often cannot write to an arbitrary folder).
    [string]$BackupDirectory,

    [System.Management.Automation.PSCredential]$Credential,
    [switch]$KeepRestored
)

$ErrorActionPreference = 'Stop'

function New-Connection([string]$catalog) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server
    $builder['Initial Catalog'] = $catalog
    $builder['Connect Timeout'] = 30
    if ($Credential) {
        $builder['User ID'] = $Credential.UserName
        $builder['Password'] = $Credential.GetNetworkCredential().Password
    } else {
        $builder['Integrated Security'] = $true
    }
    $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    $connection.Open()
    return $connection
}

function Invoke-Sql([string]$catalog, [string]$sql, [int]$timeoutSeconds = 3600) {
    $connection = New-Connection $catalog
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $sql
        $command.CommandTimeout = $timeoutSeconds
        $table = New-Object System.Data.DataTable
        $reader = $command.ExecuteReader()
        $table.Load($reader)
        return ,$table
    } finally {
        $connection.Dispose()
    }
}

function Get-Fingerprint([string]$catalog) {
    $migrations = Invoke-Sql $catalog "SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId]"

    # Exact counts from the partition metadata: no table scans, safe on a large live database.
    $counts = Invoke-Sql $catalog @"
SELECT s.name + '.' + t.name AS [Table], SUM(p.rows) AS [Rows]
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
GROUP BY s.name, t.name
ORDER BY [Table]
"@

    $rows = @{}
    foreach ($row in $counts.Rows) { $rows[[string]$row.Table] = [long]$row.Rows }

    return [pscustomobject]@{
        Migrations = @($migrations.Rows | ForEach-Object { [string]$_.MigrationId })
        Rows       = $rows
    }
}

function Quote([string]$name) { return '[' + $name.Replace(']', ']]') + ']' }
function Literal([string]$text) { return "N'" + $text.Replace("'", "''") + "'" }

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$restoredName = "$($Database)_restorecheck_$stamp"

Write-Host "Share7 restore rehearsal - $Server / $Database" -ForegroundColor Cyan

# 1 -- fingerprint the source ----------------------------------------------------------------
$source = Get-Fingerprint $Database
Write-Host ("  source: {0} migrations, {1} tables, {2:N0} rows" -f $source.Migrations.Count, $source.Rows.Count, ($source.Rows.Values | Measure-Object -Sum).Sum)

if (-not $BackupDirectory) {
    $BackupDirectory = [string](Invoke-Sql 'master' "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS [Path]").Rows[0].Path
}
$backupFile = Join-Path $BackupDirectory "$($Database)_$stamp.bak"

# 2 -- back up -------------------------------------------------------------------------------
$timer = [System.Diagnostics.Stopwatch]::StartNew()
Invoke-Sql 'master' "BACKUP DATABASE $(Quote $Database) TO DISK = $(Literal $backupFile) WITH COPY_ONLY, CHECKSUM, INIT, NAME = $(Literal "Share7 restore rehearsal $stamp")" | Out-Null
Write-Host ("  backed up to {0} in {1:N1}s" -f $backupFile, $timer.Elapsed.TotalSeconds)

# 3 -- verify the file -----------------------------------------------------------------------
Invoke-Sql 'master' "RESTORE VERIFYONLY FROM DISK = $(Literal $backupFile) WITH CHECKSUM" | Out-Null
Write-Host '  backup file verified (checksum)'

# 4 -- restore under a new name --------------------------------------------------------------
$dataPath = [string](Invoke-Sql 'master' "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS [Path]").Rows[0].Path
$files = Invoke-Sql 'master' "RESTORE FILELISTONLY FROM DISK = $(Literal $backupFile)"

$moves = @()
foreach ($file in $files.Rows) {
    $extension = if ([string]$file.Type -eq 'L') { '.ldf' } else { '.mdf' }
    $target = Join-Path $dataPath ("{0}_{1}{2}" -f $restoredName, $file.FileId, $extension)
    $moves += "MOVE $(Literal ([string]$file.LogicalName)) TO $(Literal $target)"
}

$timer.Restart()
Invoke-Sql 'master' "RESTORE DATABASE $(Quote $restoredName) FROM DISK = $(Literal $backupFile) WITH $($moves -join ', '), CHECKSUM, RECOVERY" | Out-Null
Write-Host ("  restored as {0} in {1:N1}s" -f $restoredName, $timer.Elapsed.TotalSeconds)

# 5 -- compare -------------------------------------------------------------------------------
try {
    $restored = Get-Fingerprint $restoredName
    $problems = @()

    if (($source.Migrations -join '|') -ne ($restored.Migrations -join '|')) {
        $problems += 'The applied migrations differ.'
    }

    foreach ($table in ($source.Rows.Keys + $restored.Rows.Keys | Sort-Object -Unique)) {
        $before = $source.Rows[$table]
        $after = $restored.Rows[$table]
        if ($before -ne $after) { $problems += "$table : $before row(s) in the source, $after in the restore" }
    }

    if ($problems.Count -gt 0) {
        Write-Host '  RESTORE DOES NOT MATCH THE SOURCE:' -ForegroundColor Red
        $problems | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        Write-Host '  (A live source that is being written to will differ slightly; rehearse against a quiet copy or during a quiet window.)' -ForegroundColor Yellow
        exit 1
    }

    Write-Host ("  MATCH: {0} migrations and {1} tables identical" -f $restored.Migrations.Count, $restored.Rows.Count) -ForegroundColor Green
}
finally {
    if (-not $KeepRestored) {
        Invoke-Sql 'master' "ALTER DATABASE $(Quote $restoredName) SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $(Quote $restoredName);" | Out-Null
        Write-Host "  dropped $restoredName"
    }
}

Write-Host "Backup kept at $backupFile - delete it when you no longer need it." -ForegroundColor Cyan
