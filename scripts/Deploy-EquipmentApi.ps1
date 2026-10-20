#Requires -RunAsAdministrator
param(
    [Parameter(Mandatory)][string]$OperatorName,
    [string]$PublishPath = (Join-Path $PSScriptRoot '..\artifacts\equipment-api'),
    [ValidateSet('Quality','Logistics','Maintenance','Supervisor')][string]$Role
)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $PSScriptRoot '..\artifacts\equipment-api-deployment.log') -Append
$serviceName = 'TransportOrders.Runtime'
$packageRoot = (Resolve-Path -LiteralPath $PublishPath).Path
$oldBinaryPath = (Get-CimInstance Win32_Service -Filter "Name='$serviceName'").PathName
if (-not $oldBinaryPath) { throw 'The central runtime service must already be installed.' }
if ($oldBinaryPath -notmatch '^"([^"]+)"' -and $oldBinaryPath -notmatch '^(.+?\.exe)(?:\s|$)') {
    throw 'Cannot resolve the installed service executable path.'
}
$sourceConfiguration = Join-Path (Split-Path -Parent $Matches[1]) 'appsettings.Production.json'
$configuration = Get-Content -LiteralPath $sourceConfiguration -Raw | ConvertFrom-Json
if (-not $configuration.Plant) {
    $configuration | Add-Member -MemberType NoteProperty -Name Plant -Value ([pscustomobject]@{})
}
if (-not $configuration.Plant.Id) {
    $configuration.Plant | Add-Member -MemberType NoteProperty -Name Id -Value ([guid]::NewGuid().ToString())
}
if (-not $configuration.Backup) {
    $configuration | Add-Member -MemberType NoteProperty -Name Backup -Value @{
        Enabled = $true; IntervalHours = 24
        Directory = (Join-Path (Split-Path -Parent $configuration.Database.Path) 'backups')
    }
}
$existingOperators = @()
if ($configuration.EquipmentControl) { $existingOperators = @($configuration.EquipmentControl.AllowedOperators) }
$operators = @($existingOperators + $OperatorName | Select-Object -Unique)
$configuration | Add-Member -MemberType NoteProperty -Name EquipmentControl -Value @{ AllowedOperators = $operators } -Force
$commandOperators = @()
if ($configuration.OperatorCommands) { $commandOperators = @($configuration.OperatorCommands.AllowedOperators) }
$configuration | Add-Member -MemberType NoteProperty -Name OperatorCommands -Value @{
    AllowedOperators = @($commandOperators + $OperatorName | Select-Object -Unique)
} -Force
if ($Role) {
    if (-not $configuration.Access) {
        $configuration | Add-Member -MemberType NoteProperty -Name Access -Value ([pscustomobject]@{})
    }
    $configuration.Access | Add-Member -MemberType NoteProperty -Name UseRoles -Value $true -Force
    if (-not $configuration.Access.Roles) {
        $configuration.Access | Add-Member -MemberType NoteProperty -Name Roles -Value ([pscustomobject]@{})
    }
    $existingRole = $configuration.Access.Roles.$Role
    $users = @(); $groups = @()
    if ($existingRole) { $users = @($existingRole.Users); $groups = @($existingRole.Groups) }
    $configuration.Access.Roles | Add-Member -MemberType NoteProperty -Name $Role -Value @{
        Users = @($users + $OperatorName | Select-Object -Unique); Groups = $groups
    } -Force
}
$configuration | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $packageRoot 'appsettings.Production.json') -Encoding utf8
& icacls.exe $packageRoot /grant '*S-1-5-19:(OI)(CI)RX'
if ($LASTEXITCODE -ne 0) { throw 'Cannot grant LocalService read access to the package.' }
$service = Get-Service $serviceName
if ($service.Status -ne 'Stopped') {
    Stop-Service $serviceName
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}
$binaryPath = '"' + (Join-Path $packageRoot 'TransportOrders.Api.exe') + '" --environment Production --contentRoot "' + $packageRoot + '"'
try {
    & sc.exe config $serviceName binPath= $binaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Cannot switch the service package.' }
    Start-Service $serviceName
    (Get-Service $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    $ready = $false
    foreach ($poll in 1..15) {
        try {
            $status = Invoke-RestMethod 'http://localhost:5080/api/runtime' -UseDefaultCredentials -TimeoutSec 2
            if ($status.running -and $status.databasePath -eq $configuration.Database.Path) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'Updated runtime did not become ready.' }
    $status | ConvertTo-Json
    Write-Host "Equipment and operator commands authorized: $OperatorName"
} catch {
    $deploymentError = $_
    Stop-Service $serviceName -ErrorAction SilentlyContinue
    & sc.exe config $serviceName binPath= $oldBinaryPath
    Start-Service $serviceName -ErrorAction SilentlyContinue
    throw $deploymentError
} finally { Stop-Transcript }
