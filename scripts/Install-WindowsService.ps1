#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DatabasePath,
    [string]$PublishPath = (Join-Path $PSScriptRoot '..\artifacts\windows-service'),
    [ValidateRange(1024,65535)][int]$Port = 5080,
    [string[]]$AllowedOperators = @([Security.Principal.WindowsIdentity]::GetCurrent().Name)
)
$ErrorActionPreference = 'Stop'
$serviceName = 'TransportOrders.Runtime'
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw "$serviceName already exists. Stop and uninstall it before installing another package."
}
$publishRoot = (Resolve-Path -LiteralPath $PublishPath).Path
$executable = Join-Path $publishRoot 'TransportOrders.Api.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Publish the win-x64 package first.' }
if (-not [IO.Path]::IsPathFullyQualified($DatabasePath)) { throw 'DatabasePath must be an absolute path.' }
$databaseFile = [IO.Path]::GetFullPath($DatabasePath)
if (-not (Test-Path -LiteralPath $databaseFile -PathType Leaf)) {
    throw 'Select an existing extrusion database initialized by the central API; this installer does not silently create a new plant.'
}
$dataRoot = Split-Path -Parent $databaseFile
$configPath = Join-Path $publishRoot 'appsettings.Production.json'
if (Test-Path -LiteralPath $configPath) { throw 'Production configuration already exists; review it before replacing it.' }
$configuration = @{
    Database = @{ Path = $databaseFile }
    Plant = @{ Id = [guid]::NewGuid().ToString(); Name = 'Aluminium extrusion demo' }
    Backup = @{ Enabled = $true; IntervalHours = 24; Directory = (Join-Path $dataRoot 'backups') }
    Runtime = @{ Enabled = $true; LogDirectory = (Join-Path $dataRoot 'logs') }
    EquipmentControl = @{ AllowedOperators = $AllowedOperators }
    OperatorCommands = @{ AllowedOperators = $AllowedOperators }
    Access = @{ UseRoles = $true; Roles = @{ Supervisor = @{ Users = $AllowedOperators; Groups = @() } } }
    Urls = "http://localhost:$Port"
    Logging = @{ LogLevel = @{ Default = 'Information'; 'Microsoft' = 'Warning' } }
}
$configuration | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath -Encoding utf8
# SID avoids translated account names. SQLite needs directory access for journal/WAL files.
& icacls.exe $dataRoot /grant '*S-1-5-19:(OI)(CI)M'
if ($LASTEXITCODE -ne 0) { throw 'Cannot grant LocalService access to the database directory.' }
& icacls.exe $publishRoot /grant '*S-1-5-19:(OI)(CI)RX'
if ($LASTEXITCODE -ne 0) { throw 'Cannot grant LocalService access to the published binaries.' }
$binaryCommand = '"' + $executable + '" --environment Production --contentRoot "' + $publishRoot + '"'
& sc.exe create $serviceName binPath= $binaryCommand start= delayed-auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'TransportOrders central demo runtime'
if ($LASTEXITCODE -ne 0) { throw 'Windows service creation failed.' }
& sc.exe description $serviceName 'Persistent extrusion demo scheduler and simulated PLC exchanges.'
if ($LASTEXITCODE -ne 0) { throw 'Service description failed.' }
& sc.exe failure $serviceName reset= 86400 actions= 'restart/60000/restart/60000/restart/60000'
if ($LASTEXITCODE -ne 0) { throw 'Service recovery configuration failed.' }
& sc.exe failureflag $serviceName 1
if ($LASTEXITCODE -ne 0) { throw 'Service failure flag configuration failed.' }
Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
$status = Invoke-RestMethod -Uri "http://localhost:$Port/api/runtime" -UseDefaultCredentials -TimeoutSec 15
if (-not $status.enabled -or -not $status.running -or $status.databasePath -ne $databaseFile) {
    throw 'Service started but runtime configuration verification failed. Inspect the service log.'
}
Write-Host "Service installed and running. Database: $databaseFile"
Write-Host "Status: http://localhost:$Port/api/runtime"
