param([switch]$BootstrapAdmin, [string]$AdminEmail = 'admin@mf01.local')
$ErrorActionPreference = 'Stop'
$backendRoot = Split-Path $PSScriptRoot -Parent
if (!(Test-Path -LiteralPath (Join-Path $backendRoot 'src\Supermarket.Api\appsettings.Local.json'))) { throw 'Copy src/Supermarket.Api/appsettings.Local.example.json to appsettings.Local.json and fill in the existing database connection and Jwt:Key. See README.md for local configuration.' }
$oldPassword = $env:Bootstrap__Password
$oldEmail = $env:Bootstrap__Email
$oldEnabled = $env:Bootstrap__Enabled
try {
    if ($BootstrapAdmin) {
        $securePassword = Read-Host 'Initial Admin password (12 to 128 characters)' -AsSecureString
        $env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', $securePassword).Password
        $env:Bootstrap__Email = $AdminEmail
        $env:Bootstrap__Enabled = 'true'
    }
    Push-Location $backendRoot
    try { & dotnet run --project src/Supermarket.Api --launch-profile http; if ($LASTEXITCODE -ne 0) { throw 'API process failed.' } }
    finally { Pop-Location }
} finally {
    $env:Bootstrap__Password = $oldPassword
    $env:Bootstrap__Email = $oldEmail
    $env:Bootstrap__Enabled = $oldEnabled
}
