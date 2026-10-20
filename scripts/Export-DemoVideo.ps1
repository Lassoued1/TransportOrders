[CmdletBinding()]
param([string]$SessionPath)
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $SessionPath) { $SessionPath = Join-Path $workspace ('artifacts\demo-video-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$session = [IO.Path]::GetFullPath($SessionPath)
if (-not $session.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep the demo session inside this workspace.' }
if (Test-Path -LiteralPath $session) { throw 'Use a new session directory.' }
if (Get-NetTCPConnection -LocalPort 5097 -State Listen -ErrorAction SilentlyContinue) { throw 'Isolated demo port 5097 is already in use.' }
New-Item -ItemType Directory -Path $session | Out-Null
$site = [guid]::NewGuid().ToString()
$operator = [Security.Principal.WindowsIdentity]::GetCurrent().Name
@{ Database = @{ Path = (Join-Path $session 'demo.db') }; Plant = @{ Id = $site; Mode = 'Demo'; Name = 'Videodemonstration'; TimeZone = 'Europe/Vienna' };
    Runtime = @{ Enabled = $false; LogDirectory = (Join-Path $session 'logs') }; Backup = @{ Enabled = $false };
    Access = @{ UseRoles = $true; Roles = @{ Supervisor = @{ Users = @($operator); Groups = @() } } };
    Urls = 'http://localhost:5097' } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $session 'appsettings.Production.json') -Encoding utf8
$api = Join-Path $workspace 'artifacts\recipes-api\TransportOrders.Api.exe'
$process = Start-Process $api -WindowStyle Hidden -ArgumentList ('--environment Production --contentRoot "' + $session + '"') -PassThru `
    -RedirectStandardOutput (Join-Path $session 'api.log') -RedirectStandardError (Join-Path $session 'api.err')
try {
    $ready = $false
    foreach ($poll in 1..35) {
        if ($process.HasExited) { throw 'Demo API exited. Inspect its log.' }
        try {
            $snapshot = Invoke-RestMethod 'http://localhost:5097/api/client-state' -UseDefaultCredentials -TimeoutSec 2
            if ($snapshot.plantId -ne $site -or $snapshot.runtime.enabled) { throw 'Unexpected demo installation.' }
            $ready = $true; break
        } catch { if ($poll -eq 1 -or $poll -eq 35) { Write-Output $_.Exception.Message }; Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw 'Demo API is not ready.' }
    & dotnet run --project (Join-Path $workspace 'tools\TransportOrders.DemoVideo') -c Release -- 'http://localhost:5097' $site (Join-Path $session 'frames')
    if ($LASTEXITCODE -ne 0) { throw 'Demo frame export failed.' }
    Write-Output "DEMO_VIDEO_SESSION=$session"
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
}
