param([string]$Server = '.\SQLEXPRESS', [string]$Database = 'FA26SE103_MF01_Local')
$ErrorActionPreference = 'Stop'
if ($Database -notmatch '^FA26SE103_MF01_[A-Za-z0-9_]+$') { throw 'Use an isolated FA26SE103_MF01_* database name.' }
$backendRoot = Split-Path $PSScriptRoot -Parent
$schemaPath = Join-Path (Split-Path $backendRoot -Parent) 'DB\FA26SE103_Database_V0.1.sql'
if (!(Test-Path -LiteralPath $schemaPath)) { throw "Cannot find the shared SQL schema: $schemaPath" }
$exists = & sqlcmd -S $Server -E -C -h -1 -W -b -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID('$Database') IS NULL THEN 0 ELSE 1 END"
if ($LASTEXITCODE -ne 0) { throw 'Cannot connect to SQL Server.' }
if (($exists | Out-String).Trim() -eq '0') {
    $temporarySql = Join-Path ([IO.Path]::GetTempPath()) ("mf01-{0}.sql" -f [guid]::NewGuid())
    try {
        (Get-Content -Raw -LiteralPath $schemaPath).Replace('FA26SE103', $Database) | Set-Content -LiteralPath $temporarySql -Encoding utf8
        & sqlcmd -S $Server -E -C -b -i $temporarySql
        if ($LASTEXITCODE -ne 0) { throw 'Schema import failed.' }
    } finally { if (Test-Path -LiteralPath $temporarySql) { Remove-Item -LiteralPath $temporarySql } }
}
$localPath = Join-Path $backendRoot 'src\Supermarket.Api\appsettings.Local.json'
if (!(Test-Path -LiteralPath $localPath)) {
    $key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    @{
        ConnectionStrings = @{ SqlServer = "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True" }
        Jwt = @{ Key = $key }
        DataProtection = @{ KeyPath = (Join-Path $backendRoot '.local\keys') }
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $localPath -Encoding utf8
} else {
    $localConfig = Get-Content -Raw -LiteralPath $localPath | ConvertFrom-Json
    $localConfig.ConnectionStrings.SqlServer = "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True"
    $localConfig | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $localPath -Encoding utf8
}
Write-Host "Database ready: $Database. Local configuration: $localPath"
Write-Host 'Next: .\scripts\Start-Local.ps1 -BootstrapAdmin (first start), or .\scripts\Start-Local.ps1'
