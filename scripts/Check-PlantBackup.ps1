[CmdletBinding()]
param([string]$ServiceUrl = 'https://DORNBIRNTATA:5443', [string]$OutputPath)
$ErrorActionPreference = 'Stop'
$status = Invoke-RestMethod ($ServiceUrl.TrimEnd('/') + '/api/backups/status') -UseDefaultCredentials -TimeoutSec 15
if (-not $status.enabled -or -not $status.lastSuccessUtc -or $status.lastError) {
    throw ('A successful automatic backup is not available: ' + ($status | ConvertTo-Json -Compress))
}
$status | ConvertTo-Json
if ($OutputPath) { $status | ConvertTo-Json | Set-Content -LiteralPath $OutputPath -Encoding utf8 }
