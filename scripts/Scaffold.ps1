param([string]$Connection = $env:MF01_SCAFFOLD_CONNECTION, [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Connection)) { throw 'Set MF01_SCAFFOLD_CONNECTION to the approved SQL Server database connection.' }
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & dotnet tool restore --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'EF tool restore failed.' }
    $tableArguments = @('Role','UserAccount','Supermarket','Floor','Zone','Camera','CameraConnection','CameraZoneMapping','MonitoringConfiguration','MonitoringRule','IncidentType','CameraHealthEvent','Incident','OperationalEvent') | ForEach-Object { '--table'; $_ }
    & dotnet ef dbcontext scaffold $Connection Microsoft.EntityFrameworkCore.SqlServer --project src/Supermarket.Infrastructure --configuration $Configuration --output-dir Persistence/Scaffolded --context AppDbContext --no-onconfiguring --force @tableArguments
    if ($LASTEXITCODE -ne 0) { throw 'Scaffolding failed.' }
} finally { Pop-Location }
