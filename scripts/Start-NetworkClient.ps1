[CmdletBinding()]
param([string]$ServerName = 'DORNBIRNTATA', [int]$Port = 5443,
    [string]$PlantId = 'e4f95b8b-7ec2-4f9e-9477-c1491eccd0a5',
    [string]$ClientPath)
$ErrorActionPreference = 'Stop'
if (-not $ClientPath) { $ClientPath = Join-Path $PSScriptRoot '..\artifacts\recipes-client\TransportOrders.App.exe' }
$env:TRANSPORTORDERS_SERVICE_URL = "https://${ServerName}:$Port"
$env:TRANSPORTORDERS_PLANT_ID = $PlantId
& $ClientPath
