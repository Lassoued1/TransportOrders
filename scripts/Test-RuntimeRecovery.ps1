# Run with Windows PowerShell 5.1, without administrator rights.
[CmdletBinding()]
param([string]$PublishPath,
    [ValidateRange(1024,65535)][int]$Port = 5099)
$ErrorActionPreference = 'Stop'
if (-not $PublishPath) { $PublishPath = Join-Path $PSScriptRoot '..\artifacts\network-api' }
$package = (Resolve-Path -LiteralPath $PublishPath).Path
$executable = Join-Path $package 'TransportOrders.Api.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Publish the API package first.' }
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { throw 'Recovery test port already in use.' }
$testRoot = Join-Path (Split-Path -Parent $package) ('recovery-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$testDatabase = Join-Path $testRoot 'recovery.db'
$siteId = [guid]::NewGuid().ToString()
$operatorName = [Security.Principal.WindowsIdentity]::GetCurrent().Name
@{ Database = @{ Path = $testDatabase }; Plant = @{ Id = $siteId }; Access = @{
    UseRoles = $true; Roles = @{ Supervisor = @{ Users = @($operatorName); Groups = @() } }
}; Urls = "http://localhost:$Port" } | ConvertTo-Json -Depth 7 |
    Set-Content -LiteralPath (Join-Path $testRoot 'appsettings.Production.json') -Encoding utf8
$script:runtimeProcess = $null
$script:startNumber = 0
$url = "http://localhost:$Port"
function Stop-TestRuntime {
    if ($script:runtimeProcess -and -not $script:runtimeProcess.HasExited) {
        # Abrupt process termination deliberately exercises persisted recovery and lease release.
        Stop-Process -Id $script:runtimeProcess.Id -Force
        $script:runtimeProcess.WaitForExit()
    }
}
function Read-TestSnapshot {
    $snapshot = Invoke-RestMethod "$url/api/client-state" -UseDefaultCredentials -TimeoutSec 3
    if ($snapshot.plantId -ne $siteId) { throw 'Unexpected plant on the test port.' }
    return $snapshot
}
function Start-TestRuntime([bool]$enabled) {
    $script:startNumber++
    $arguments = '--environment Production --contentRoot "' + $testRoot + '" --Runtime:Enabled ' + $enabled.ToString().ToLowerInvariant()
    $script:runtimeProcess = Start-Process $executable -WindowStyle Hidden -ArgumentList $arguments -PassThru `
        -RedirectStandardOutput (Join-Path $testRoot ("start-$script:startNumber.log")) `
        -RedirectStandardError (Join-Path $testRoot ("start-$script:startNumber.err"))
    foreach ($poll in 1..30) {
        if ($script:runtimeProcess.HasExited) { throw 'Isolated runtime exited; inspect its log.' }
        try { return Read-TestSnapshot } catch { Start-Sleep -Milliseconds 250 }
    }
    throw 'Isolated runtime did not become ready.'
}
function Set-TestReplies($snapshot, [bool]$suppress) {
    @{ ExpectedVersion = $snapshot.state.plcSimulation.version; Connected = $true;
        SuppressAcknowledgements = $suppress; Reason = 'Isolated crash recovery acceptance test';
        ExpectedDatabasePath = ''; ExpectedPlantId = $siteId } | ConvertTo-Json |
        ForEach-Object { Invoke-RestMethod "$url/api/plc/simulation/settings" -Method Post -Body $_ -ContentType 'application/json' -UseDefaultCredentials } | Out-Null
}
try {
    $snapshot = Start-TestRuntime $false
    $command = @($snapshot.state.plcCommands)[0]
    if (-not $command -or $command.status -ne 'Pending') { throw 'Expected a pending seeded command.' }
    $commandId = $command.id
    Set-TestReplies $snapshot $true
    Stop-TestRuntime
    $snapshot = Start-TestRuntime $true
    foreach ($poll in 1..30) {
        $snapshot = Read-TestSnapshot
        $command = $snapshot.state.plcCommands | Where-Object id -eq $commandId
        if ($command.status -in @('AwaitingAck','TimedOut')) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($command.attempts -ne 1 -or $command.status -notin @('AwaitingAck','TimedOut')) { throw 'Initial send not recorded once.' }
    $ordersBefore = @($snapshot.state.orders | Where-Object transportNumber -eq $command.operation)
    Stop-TestRuntime
    $snapshot = Start-TestRuntime $true
    $restored = $snapshot.state.plcCommands | Where-Object id -eq $commandId
    if ($restored.attempts -ne 1 -or $restored.payloadHash -ne $command.payloadHash) { throw 'Restart changed the durable command.' }
    Set-TestReplies $snapshot $false
    foreach ($poll in 1..40) {
        $snapshot = Read-TestSnapshot
        $restored = $snapshot.state.plcCommands | Where-Object id -eq $commandId
        if ($restored.status -eq 'Applied') { break }
        Start-Sleep -Milliseconds 250
    }
    if ($restored.status -ne 'Applied' -or $restored.attempts -ne 1) { throw 'Late acknowledgement did not recover without resend.' }
    foreach ($order in $ordersBefore) {
        $after = $snapshot.state.orders | Where-Object orderNumber -eq $order.orderNumber
        if ($after.status -ne 'Done' -or @($after.history | Where-Object toStatus -eq 'Done').Count -ne 1) {
            throw 'Recovered order arrival was missing or duplicated.'
        }
    }
    Stop-TestRuntime
    $snapshot = Start-TestRuntime $true
    $final = $snapshot.state.plcCommands | Where-Object id -eq $commandId
    if ($final.status -ne 'Applied' -or $final.attempts -ne 1 -or @($final.history | Where-Object detail -eq 'Arrival and acknowledgement applied atomically').Count -ne 1) {
        throw 'Applied arrival was replayed after the second restart.'
    }
    [pscustomobject]@{ Passed = $true; Database = $testDatabase; PlantId = $siteId; CommandId = $commandId;
        Attempts = $final.attempts; Status = $final.status; OrdersRecovered = $ordersBefore.Count;
        AbruptRestarts = 3; ProductionServiceChanged = $false } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding utf8
    Get-Content -LiteralPath (Join-Path $testRoot 'result.json')
} finally { Stop-TestRuntime }
