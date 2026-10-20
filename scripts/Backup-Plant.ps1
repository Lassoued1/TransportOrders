[CmdletBinding()]
param([ValidateSet('backup','verify','restore-copy')][string]$Operation = 'backup',
    [string]$BackupPath, [string]$Destination, [string]$ToolPath)
$ErrorActionPreference = 'Stop'
if (-not $ToolPath) { $ToolPath = Join-Path $PSScriptRoot '..\artifacts\recipes-api\TransportOrders.Api.exe' }
$binary = (Get-CimInstance Win32_Service -Filter "Name='TransportOrders.Runtime'").PathName
if ($binary -notmatch '^"([^"]+)"' -and $binary -notmatch '^(.+?\.exe)(?:\s|$)') { throw 'Cannot find installed service.' }
$configuration = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $Matches[1]) 'appsettings.Production.json') -Raw | ConvertFrom-Json
if (-not $configuration.Plant.Id) { throw 'The installation must have a stable Plant:Id.' }
$arguments = @('--maintenance', $Operation, '--plant-id', $configuration.Plant.Id)
if ($Operation -eq 'backup') {
    if (-not $Destination) { $Destination = Join-Path (Split-Path -Parent $configuration.Database.Path) 'backups' }
    $arguments += @('--database', $configuration.Database.Path, '--destination', $Destination)
} else {
    if (-not $BackupPath) { throw 'BackupPath is required.' }
    $arguments += @('--backup', (Resolve-Path -LiteralPath $BackupPath).Path)
    if ($Operation -eq 'restore-copy') {
        if (-not $Destination) { throw 'Provide a new, unused database path as Destination.' }
        $arguments += @('--destination', $Destination)
    }
}
& $ToolPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Backup maintenance failed; inspect the error above.' }
