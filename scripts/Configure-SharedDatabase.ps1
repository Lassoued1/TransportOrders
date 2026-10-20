#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$DesktopUserSid)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $PSScriptRoot '..\artifacts\shared-database-setup.log') -Append
$databaseFile = 'C:\ProgramData\TransportOrders\extrusion.db'
$dataRoot = Split-Path -Parent $databaseFile
if (-not (Test-Path -LiteralPath $databaseFile -PathType Leaf)) { throw 'Shared database copy is missing.' }
$service = Get-Service TransportOrders.Runtime
if ($service.Status -ne 'Stopped') {
    Stop-Service TransportOrders.Runtime
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
foreach ($grant in @('*S-1-5-19:(OI)(CI)M', ('*' + $DesktopUserSid + ':(OI)(CI)M'))) {
    & icacls.exe $dataRoot /grant $grant /T
    if ($LASTEXITCODE -ne 0) { throw 'Cannot grant shared data access.' }
}
$configPath = Join-Path $PSScriptRoot '..\artifacts\windows-service\appsettings.Production.json'
Copy-Item -LiteralPath $configPath -Destination ($configPath + '.before-shared-data') -ErrorAction Stop
$configuration = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$configuration.Database.Path = $databaseFile
$configuration.Runtime.LogDirectory = Join-Path $dataRoot 'logs'
$configuration | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath -Encoding utf8
Start-Service TransportOrders.Runtime
(Get-Service TransportOrders.Runtime).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
$ready = $false
foreach ($poll in 1..15) {
    try {
        $status = Invoke-RestMethod 'http://localhost:5080/api/runtime' -UseDefaultCredentials -TimeoutSec 2
        if ($status.running -and $status.databasePath -eq $databaseFile) { $ready = $true; break }
    } catch { Start-Sleep -Milliseconds 500 }
}
if (-not $ready) { throw 'Shared service runtime did not become ready. Inspect the service logs.' }
$status | ConvertTo-Json
Stop-Transcript
