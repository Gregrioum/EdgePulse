param(
    [string]$DotNet = 'dotnet',
    [string]$Iscc = 'C:\Users\wbchoi\AppData\Local\Programs\Inno Setup 6\ISCC.exe',
    [string[]]$Architectures = @('x64', 'arm64')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Iscc)) { $Iscc = 'ISCC.exe' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $projectRoot 'release'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
foreach ($architecture in $Architectures) {
    if ($architecture -notin @('x64','arm64')) { throw 'Only x64 and arm64 packages are supported.' }
    $runtimeId = 'win-' + $architecture
    $publishRoot = Join-Path $projectRoot ('work\publish\' + $runtimeId)
    & $DotNet publish (Join-Path $projectRoot 'src\EdgePulse\EdgePulse.csproj') -c Release -r $runtimeId --self-contained true -o $publishRoot --nologo
    if ($LASTEXITCODE -ne 0) { throw ('Publish failed: ' + $runtimeId) }
    $portableRoot = Join-Path $releaseRoot ('EdgePulse-1.2.0-' + $runtimeId + '-Portable')
    New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $publishRoot 'EdgePulse.exe') -Destination $portableRoot
    foreach ($name in @('README.txt','LICENSE.txt','THIRD-PARTY-NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $portableRoot }
    # Keep the runtime's distribution notices with every package.
    $sdkRoot = if (Test-Path -LiteralPath $DotNet) { Split-Path -Parent $DotNet } else { Split-Path -Parent (Get-Command $DotNet).Source }
    Copy-Item -LiteralPath (Join-Path $sdkRoot 'LICENSE.txt') -Destination (Join-Path $portableRoot 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $sdkRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $portableRoot 'DOTNET-NOTICES.txt')
    Copy-Item -LiteralPath (Join-Path $portableRoot 'DOTNET-LICENSE.txt') -Destination $publishRoot
    Copy-Item -LiteralPath (Join-Path $portableRoot 'DOTNET-NOTICES.txt') -Destination $publishRoot
    Set-Content -LiteralPath (Join-Path $portableRoot 'portable.flag') -Value 'Keep this file beside EdgePulse.exe to store preferences in Data.' -Encoding utf8
    # Package only distributable files; the live portable folder may contain user preferences.
    $archiveRoot = Join-Path $projectRoot ('work\package-staging\' + [Guid]::NewGuid().ToString('N') + '\' + (Split-Path -Leaf $portableRoot))
    New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
    foreach ($name in @('EdgePulse.exe','README.txt','LICENSE.txt','THIRD-PARTY-NOTICES.txt','DOTNET-LICENSE.txt','DOTNET-NOTICES.txt','portable.flag')) {
        Copy-Item -LiteralPath (Join-Path $portableRoot $name) -Destination $archiveRoot
    }
    Compress-Archive -LiteralPath $archiveRoot -DestinationPath ($portableRoot + '.zip') -Force
    & $Iscc ('-dArch=' + $architecture) ('-dPayload=' + $publishRoot) ('-dDestination=' + $releaseRoot) (Join-Path $projectRoot 'packaging\EdgePulse.iss') /Q
    if ($LASTEXITCODE -ne 0) { throw ('Installer build failed: ' + $runtimeId) }
}
Get-ChildItem -LiteralPath $releaseRoot -File | Where-Object Extension -In '.zip','.exe' | ForEach-Object {
    $fileHash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    $fileHash.Hash.ToLowerInvariant() + '  ' + $_.Name
} | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii
