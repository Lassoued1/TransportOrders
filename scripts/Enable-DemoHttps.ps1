#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$ServerName = 'DORNBIRNTATA', [ValidateRange(1024,65535)][int]$Port = 5443)
$ErrorActionPreference = 'Stop'
if ($ServerName -ne $env:COMPUTERNAME) { throw 'Run this script on the named server.' }
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $workspace 'artifacts\network-https'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Start-Transcript -Path (Join-Path $output 'installation.log') -Append
try {
    $serviceName = 'TransportOrders.Runtime'
    $binaryPath = (Get-CimInstance Win32_Service -Filter "Name='$serviceName'").PathName
    if ($binaryPath -notmatch '^"([^"]+)"' -and $binaryPath -notmatch '^(.+?\.exe)(?:\s|$)') { throw 'Cannot resolve service package.' }
    $configurationPath = Join-Path (Split-Path -Parent $Matches[1]) 'appsettings.Production.json'
    $original = Get-Content -LiteralPath $configurationPath -Raw
    $configuration = $original | ConvertFrom-Json
    if (-not $configuration.Plant.Id) { throw 'Deploy the network API first, with a persistent Plant:Id.' }
    $certificate = @(Get-ChildItem Cert:\LocalMachine\My | Where-Object {
        $_.FriendlyName -eq 'TransportOrders demo HTTPS' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30)
    })
    if ($certificate.Count -gt 1) { throw 'Multiple demo certificates found; review the store first.' }
    if ($certificate.Count -eq 0) {
        $certificate = New-SelfSignedCertificate -DnsName $ServerName,'localhost' -Type SSLServerAuthentication `
            -CertStoreLocation Cert:\LocalMachine\My -FriendlyName 'TransportOrders demo HTTPS' `
            -Provider 'Microsoft RSA SChannel Cryptographic Provider' -KeyAlgorithm RSA -KeyLength 2048 `
            -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(1)
    } else { $certificate = $certificate[0] }
    # Give the runtime read access to this key only; export contains no private key.
    $keyName = $certificate.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
    if (-not $keyName) { throw 'Expected the demo certificate to use an RSA machine key.' }
    $keyPath = Join-Path $env:ProgramData ('Microsoft\Crypto\RSA\MachineKeys\' + $keyName)
    if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) { throw 'Machine private key not found.' }
    & icacls.exe $keyPath /grant '*S-1-5-19:R'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot grant the service read access to its certificate key.' }
    $publicCertificate = Join-Path $output ($ServerName + '.cer')
    Export-Certificate -Cert $certificate -FilePath $publicCertificate -Force | Out-Null
    if (-not (Test-Path ('Cert:\LocalMachine\Root\' + $certificate.Thumbprint))) {
        Import-Certificate -FilePath $publicCertificate -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    }
    $configuration | Add-Member -MemberType NoteProperty -Name Kestrel -Force -Value @{
        Endpoints = @{
            Local = @{ Url = 'http://localhost:5080' }
            Operators = @{ Url = "https://*:$Port"; Certificate = @{
                Subject = $ServerName; Store = 'My'; Location = 'LocalMachine'; AllowInvalid = $false
            } }
        }
    }
    Set-Content -LiteralPath (Join-Path $output 'appsettings.before-https.json') -Value $original -Encoding utf8
    $configuration | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configurationPath -Encoding utf8
    try {
        Restart-Service $serviceName
        (Get-Service $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        $ready = $false
        foreach ($poll in 1..20) {
            try {
                $snapshot = Invoke-RestMethod "https://${ServerName}:$Port/api/client-state" -UseDefaultCredentials -TimeoutSec 2
                if ($snapshot.plantId -eq $configuration.Plant.Id) { $ready = $true; break }
            } catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $ready) { throw 'HTTPS identity verification failed.' }
        # Restrict the demonstration port to private networks and local-subnet clients.
        $ruleName = 'TransportOrders-Demo-HTTPS'
        if (-not (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -Name $ruleName -DisplayName 'TransportOrders demo HTTPS' -Direction Inbound `
                -Action Allow -Protocol TCP -LocalPort $Port -Profile Private -RemoteAddress LocalSubnet | Out-Null
        }
        [pscustomobject]@{ Url = "https://${ServerName}:$Port"; PlantId = $snapshot.plantId;
            User = $snapshot.access.user; CertificateThumbprint = $certificate.Thumbprint;
            CertificateExpires = $certificate.NotAfter; PublicCertificate = $publicCertificate;
            NetworkProfiles = @(Get-NetConnectionProfile | Select-Object Name,NetworkCategory)
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
    } catch {
        Set-Content -LiteralPath $configurationPath -Value $original -Encoding utf8
        Restart-Service $serviceName -ErrorAction SilentlyContinue
        throw
    }
} finally { Stop-Transcript }
