#Requires -RunAsAdministrator
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$serviceName = 'TransportOrders.Runtime'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service) { Write-Host 'Service is not installed.'; return }
if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
& sc.exe delete $serviceName
if ($LASTEXITCODE -ne 0) { throw 'Windows service deletion failed.' }
Write-Host 'Service removed. Database, logs, binaries and directory permissions have been preserved.'
