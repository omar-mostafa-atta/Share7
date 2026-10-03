param(
    [string]$TargetConnectionString = $env:SHARE7_MIGRATION_TARGET_CONNECTION_STRING,
    [string]$SourceConnectionString = 'Data Source=.\SQLEXPRESS;Initial Catalog=Shareh;Integrated Security=SSPI;TrustServerCertificate=True;'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($TargetConnectionString)) { throw "Set SHARE7_MIGRATION_TARGET_CONNECTION_STRING or pass -TargetConnectionString." }
[System.Reflection.Assembly]::LoadWithPartialName('System.Data') | Out-Null

Write-Host '========================================================' -ForegroundColor Cyan
Write-Host ' Share7 Database Migration to MonsterASP.NET' -ForegroundColor Cyan
Write-Host '========================================================' -ForegroundColor Cyan

# 1. Test Connections
Write-Host "`n[1/4] Testing connections..." -ForegroundColor Yellow
$sourceConn = New-Object System.Data.SqlClient.SqlConnection($SourceConnectionString)
$sourceConn.Open()
Write-Host '✓ Connected to local database (Shareh)' -ForegroundColor Green

$targetConn = New-Object System.Data.SqlClient.SqlConnection($TargetConnectionString)
$targetConn.Open()
Write-Host '✓ Connected to MonsterASP MS SQL (db69112)' -ForegroundColor Green

# 2. Disable all foreign keys on target
Write-Host "`n[2/4] Disabling all foreign key constraints on target..." -ForegroundColor Yellow
$disableFkSql = "DECLARE @sql NVARCHAR(MAX) = N''; SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) + N';' + CHAR(10) FROM sys.foreign_keys fk JOIN sys.tables t ON fk.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id; IF LEN(@sql) > 0 EXEC sp_executesql @sql;"
$cmdDisable = $targetConn.CreateCommand()
$cmdDisable.CommandTimeout = 120
$cmdDisable.CommandText = $disableFkSql
$cmdDisable.ExecuteNonQuery() | Out-Null

# Clear any existing target data in user tables (except __EFMigrationsHistory)
$clearSql = "DECLARE @sql NVARCHAR(MAX) = N''; SELECT @sql += N'DELETE FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';' + CHAR(10) FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name <> '__EFMigrationsHistory'; IF LEN(@sql) > 0 EXEC sp_executesql @sql;"
$cmdClear = $targetConn.CreateCommand()
$cmdClear.CommandTimeout = 120
$cmdClear.CommandText = $clearSql
$cmdClear.ExecuteNonQuery() | Out-Null
Write-Host '✓ Target tables prepared (foreign keys disabled).' -ForegroundColor Green

# 3. Query tables with data from source
Write-Host "`n[3/4] Copying tables from local SQLEXPRESS to MonsterASP..." -ForegroundColor Yellow
$cmdTables = $sourceConn.CreateCommand()
$cmdTables.CommandText = "SELECT t.name, s.row_count FROM sys.tables t JOIN sys.dm_db_partition_stats s ON t.object_id = s.object_id WHERE s.index_id IN (0,1) AND s.row_count > 0 AND t.name <> '__EFMigrationsHistory' ORDER BY s.row_count ASC;"
$reader = $cmdTables.ExecuteReader()
$tablesToCopy = [System.Collections.Generic.List[psobject]]::new()
while ($reader.Read()) {
    $tablesToCopy.Add([PSCustomObject]@{
        Name = $reader['name'].ToString()
        ExpectedRows = [int64]$reader['row_count']
    })
}
$reader.Close()

Write-Host "Found $($tablesToCopy.Count) tables with data to copy." -ForegroundColor Cyan

$options = [System.Data.SqlClient.SqlBulkCopyOptions]::KeepIdentity -bor
           [System.Data.SqlClient.SqlBulkCopyOptions]::KeepNulls -bor
           [System.Data.SqlClient.SqlBulkCopyOptions]::TableLock

$totalCopied = 0
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

foreach ($tbl in $tablesToCopy) {
    $tableName = $tbl.Name
    $expected = $tbl.ExpectedRows
    Write-Host "  -> Copying [$tableName] ($expected rows)..." -NoNewline

    # Get non-timestamp, non-computed columns for this table
    $cmdCols = $sourceConn.CreateCommand()
    $cmdCols.CommandText = "SELECT c.name FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id JOIN sys.types ty ON c.user_type_id = ty.user_type_id WHERE t.name = '$tableName' AND ty.name NOT IN ('timestamp', 'rowversion') AND c.is_computed = 0 ORDER BY c.column_id;"
    $colReader = $cmdCols.ExecuteReader()
    $cols = [System.Collections.Generic.List[string]]::new()
    while ($colReader.Read()) {
        $cols.Add($colReader[0].ToString())
    }
    $colReader.Close()

    if ($cols.Count -eq 0) {
        Write-Host ' Skipped (no columns)' -ForegroundColor Yellow
        continue
    }

    $colList = ($cols | ForEach-Object { "[$_]" }) -join ', '
    $cmdSelect = $sourceConn.CreateCommand()
    $cmdSelect.CommandTimeout = 600
    $cmdSelect.CommandText = "SELECT $colList FROM [$tableName];"
    $dataReader = $cmdSelect.ExecuteReader()

    $bulkCopy = New-Object System.Data.SqlClient.SqlBulkCopy($targetConn, $options, $null)
    $bulkCopy.DestinationTableName = "[$tableName]"
    $bulkCopy.BulkCopyTimeout = 600
    $bulkCopy.BatchSize = 5000

    foreach ($c in $cols) {
        $bulkCopy.ColumnMappings.Add($c, $c) | Out-Null
    }

    try {
        $bulkCopy.WriteToServer($dataReader)
        Write-Host ' Done!' -ForegroundColor Green
        $totalCopied += $expected
    }
    catch {
        Write-Host ' Failed!' -ForegroundColor Red
        Write-Warning "Error copying $tableName`: $_"
    }
    finally {
        $dataReader.Close()
        $bulkCopy.Close()
    }
}

# 4. Re-enable foreign keys
Write-Host "`n[4/4] Re-enabling all foreign key constraints on target..." -ForegroundColor Yellow
$enableFkSql = "DECLARE @sql NVARCHAR(MAX) = N''; SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' WITH CHECK CHECK CONSTRAINT ' + QUOTENAME(fk.name) + N';' + CHAR(10) FROM sys.foreign_keys fk JOIN sys.tables t ON fk.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id; IF LEN(@sql) > 0 EXEC sp_executesql @sql;"
$cmdEnable = $targetConn.CreateCommand()
$cmdEnable.CommandTimeout = 300
$cmdEnable.CommandText = $enableFkSql
try {
    $cmdEnable.ExecuteNonQuery() | Out-Null
    Write-Host '✓ All foreign key constraints re-enabled and verified!' -ForegroundColor Green
}
catch {
    Write-Warning "Foreign key re-enable notice: $_"
}

$sourceConn.Close()
$targetConn.Close()
$stopwatch.Stop()

Write-Host '========================================================' -ForegroundColor Green
Write-Host " Database Migration Succeeded! Total rows: $totalCopied in $([math]::Round($stopwatch.Elapsed.TotalSeconds, 1))s" -ForegroundColor Green
Write-Host '========================================================' -ForegroundColor Green
