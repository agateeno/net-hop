<#
.SYNOPSIS
    Builds the NetHop MSI on this PC, the way CI does.

.DESCRIPTION
    For each architecture:
      1. publishes the service, the CLI and the app into one folder (out\<arch>\NetHop\bin), sharing one
         self-contained .NET runtime, as CI's "Publish" step does;
      2. builds the Windows 11 context menu handler and its package into that folder
         (Build-ContextMenuPackage.ps1, needs Visual Studio with the C++ tools and the Windows SDK; without them
         the MSI is built anyway and only the classic "Apply with NetHop" entry is there);
      3. builds installer\NetHop.Installer.wixproj into out\msi.

    Needs the .NET 10 SDK. The MSI is not signed.

.PARAMETER Version
    The product version (1.2.3). By default, the patch after the latest release tag (v0.5.0 gives 0.5.1), so that
    the MSI installs over the released version instead of being refused as a downgrade.

.PARAMETER Architecture
    x64, arm64 or all. By default, this PC's architecture.

.PARAMETER Release
    Build as a release: the app's update check then treats it as one. Without it, it is a development build,
    which is never offered updates.

.PARAMETER SkipContextMenu
    Do not build the Windows 11 context menu package.

.EXAMPLE
    .\scripts\Build-Msi.ps1

.EXAMPLE
    .\scripts\Build-Msi.ps1 -Version 0.6.0 -Architecture all
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [ValidateSet('x64', 'arm64', 'all')][string]$Architecture,
    [switch]$Release,
    [switch]$SkipContextMenu
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $Version) {
    $latest = git -C $root tag --list 'v*' |
        Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } |
        ForEach-Object { [version]$_.Substring(1) } |
        Sort-Object -Descending | Select-Object -First 1
    $Version = if ($latest) { '{0}.{1}.{2}' -f $latest.Major, $latest.Minor, ($latest.Build + 1) } else { '0.1.0' }
}
if (-not $Architecture) {
    $Architecture = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
}
$architectures = if ($Architecture -eq 'all') { 'x64', 'arm64' } else { @($Architecture) }
$buildKind = if ($Release) { 'release' } else { 'development' }
$msiFolder = Join-Path $root 'out\msi'

Write-Host "NetHop $Version ($buildKind build): $($architectures -join ', ')"

foreach ($arch in $architectures) {
    $bin = Join-Path $root "out\$arch\NetHop\bin"
    $platform = if ($arch -eq 'arm64') { 'ARM64' } else { 'x64' }

    # Files left by an earlier build would end up in the MSI.
    if (Test-Path $bin) { Remove-Item $bin -Recurse -Force }

    Write-Host "`n${arch}: publishing..."
    dotnet publish (Join-Path $root 'src\NetHop.Service') -c Release -r win-$arch --self-contained -p:Version=$Version -o $bin
    if ($LASTEXITCODE -ne 0) { throw "Publishing the service failed for $arch." }
    dotnet publish (Join-Path $root 'src\NetHop.Cli') -c Release -r win-$arch --self-contained -p:Version=$Version -o $bin
    if ($LASTEXITCODE -ne 0) { throw "Publishing the CLI failed for $arch." }
    dotnet publish (Join-Path $root 'src\NetHop.App') -c Release -r win-$arch -p:Platform=$platform --self-contained `
        -p:Version=$Version -p:NetHopBuildKind=$buildKind -o $bin
    if ($LASTEXITCODE -ne 0) { throw "Publishing the app failed for $arch." }

    # Without its .pri (compiled XAML and strings) the app crashes at start.
    if (-not (Get-ChildItem $bin -Filter *.pri | Where-Object Name -in 'NetHop.App.pri', 'resources.pri')) {
        throw "${arch}: the app's .pri file is missing from the build."
    }

    if (-not $SkipContextMenu) {
        try {
            & (Join-Path $PSScriptRoot 'Build-ContextMenuPackage.ps1') -BinDirectory $bin -Architecture $arch -Version $Version
        }
        catch {
            Write-Warning "Context menu package skipped: $($_.Exception.Message) The MSI keeps the classic menu entry."
        }
    }

    Write-Host "`n${arch}: building the MSI..."
    dotnet build (Join-Path $root 'installer\NetHop.Installer.wixproj') -c Release `
        -p:NetHopBinDir=$bin -p:ProductVersion=$Version -p:InstallerPlatform=$arch -o $msiFolder
    if ($LASTEXITCODE -ne 0) { throw "The MSI failed to build for $arch." }
}

Write-Host ''
Get-ChildItem $msiFolder -Filter "NetHop-$Version-*.msi" | ForEach-Object { Write-Host "Built $($_.FullName)" }
