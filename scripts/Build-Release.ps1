#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\releases')
)

$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$revision = & git -C $sourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source revision.' }
$sourceChanges = @(& git -C $sourceRoot status --porcelain --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source status.' }
$sdk = & dotnet --version
if ($LASTEXITCODE -ne 0) { throw 'The .NET SDK is required.' }

# Every invocation has its own directory; existing packages are never overwritten.
$releaseRoot = Join-Path ([IO.Path]::GetFullPath($OutputPath)) ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Push-Location $sourceRoot
try {
    & dotnet restore TransportOrders.sln
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet build TransportOrders.sln --configuration Release --no-restore -warnaserror -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & dotnet test TransportOrders.sln --configuration Release --no-build --logger trx --results-directory (Join-Path $releaseRoot 'TestResults') -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; no release manifest will be produced.' }

    foreach ($component in @('Api', 'App')) {
        $packageName = if ($component -eq 'Api') { 'api' } else { 'client' }
        $packagePath = Join-Path $releaseRoot $packageName
        & dotnet publish "src/TransportOrders.$component/TransportOrders.$component.csproj" --configuration Release --no-build --no-restore --output $packagePath
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $component" }
        Compress-Archive -Path (Join-Path $packagePath '*') -DestinationPath (Join-Path $releaseRoot "$packageName.zip")
    }

    $packages = @(foreach ($name in @('api.zip', 'client.zip')) {
        $file = Get-Item -LiteralPath (Join-Path $releaseRoot $name)
        [ordered]@{ name = $name; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    })
    [ordered]@{
        revision = $revision.Trim()
        sourceDirty = $sourceChanges.Count -gt 0
        sourceChanges = $sourceChanges
        sdk = $sdk.Trim()
        createdUtc = [DateTime]::UtcNow.ToString('o')
        configuration = 'Release'
        deployment = 'Framework-dependent Windows; .NET 8 ASP.NET Core and Desktop runtimes required'
        packages = $packages
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot 'manifest.json') -Encoding UTF8
    Write-Host "Release verified and packaged: $releaseRoot"
} finally {
    Pop-Location
}
