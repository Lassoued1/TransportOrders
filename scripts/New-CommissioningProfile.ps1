[CmdletBinding()]
param([string]$OutputDirectory, [string]$DatabasePath, [string]$OperatorName,
    [ValidateRange(1024,65535)][int]$Port = 5096)
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '..\artifacts\commissioning' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$root = (Resolve-Path -LiteralPath $OutputDirectory).Path
if (-not $DatabasePath) { $DatabasePath = Join-Path $root 'commissioning.db' }
if (-not [IO.Path]::IsPathRooted($DatabasePath)) { throw 'Use an absolute DatabasePath.' }
if (Test-Path -LiteralPath $DatabasePath) { throw 'Choose a new database for the commissioning profile.' }
$configurationPath = Join-Path $root 'appsettings.Production.json'
if (Test-Path -LiteralPath $configurationPath) { throw 'Commissioning profile already exists; review it before changing it.' }
if (-not $OperatorName) { $OperatorName = [Security.Principal.WindowsIdentity]::GetCurrent().Name }
$siteId = [guid]::NewGuid().ToString()
@{
    Plant = @{ Id = $siteId; Name = 'Site commissioning'; Mode = 'Commissioning' }
    Database = @{ Path = $DatabasePath }
    Runtime = @{ Enabled = $false; LogDirectory = (Join-Path $root 'logs') }
    Backup = @{ Enabled = $true; IntervalHours = 24; Directory = (Join-Path $root 'backups') }
    Urls = "http://localhost:$Port"
    Access = @{ UseRoles = $true; Roles = @{ Supervisor = @{ Users = @($OperatorName); Groups = @() } } }
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $configurationPath -Encoding utf8
@{
    PlantId = $siteId; Stations = @(); PlcEndpoints = @(); SignalMappings = @(); OvenRecipes = @();
    PackagingRules = @(); Erp = @{ Product = $null; Interface = $null }; AcceptanceApproved = $false
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root 'site-requirements.json') -Encoding utf8
Write-Output "Profile created: $configurationPath"
Write-Output "Plant ID: $siteId"
Write-Output 'Read-only profile. No demo data, simulator runtime or physical commands are enabled.'
