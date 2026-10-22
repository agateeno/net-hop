<#
.SYNOPSIS
    Builds the Windows 11 context menu handler and its sparse package into a NetHop bin folder.

.DESCRIPTION
    Windows 11 shows first-level context menu entries only for handlers declared by a package. This script:
      1. builds src\NetHop.ShellExtension (C++, IExplorerCommand) for the architecture,
      2. copies NetHop.ShellExtension.dll into the bin folder,
      3. packs AppxManifest.xml and its logos into NetHop.ContextMenu.msix (makeappx), also in the bin folder.
    The app registers the package for each user with the bin folder as its external location.

    Windows installs the package only when it is signed by a certificate the machine trusts, with a Publisher
    equal to the certificate's subject. Without -Publisher the package uses Windows' "unsigned package"
    publisher (OID.2.25.311729368913984317654407730594956997722=1), which the app registers with AllowUnsigned:
    Windows accepts that only where unsigned packages are allowed (for example with Developer Mode on).

.PARAMETER BinDirectory
    The published NetHop folder (NetHop.App.exe and the rest).

.PARAMETER Architecture
    x64 or arm64, the same as the binaries in BinDirectory.

.PARAMETER Version
    The product version (1.2.3); the package gets 1.2.3.0.

.PARAMETER Publisher
    The subject of the signing certificate, for example "CN=Jane Doe, O=Jane Doe, L=Rome, C=IT".

.EXAMPLE
    .\Build-ContextMenuPackage.ps1 -BinDirectory ..\out\x64\NetHop\bin -Architecture x64 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BinDirectory,
    [Parameter(Mandatory = $true)][ValidateSet('x64', 'arm64')][string]$Architecture,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Publisher
)

$ErrorActionPreference = 'Stop'
$BinDirectory = [IO.Path]::GetFullPath($BinDirectory)
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$source = Join-Path $repo 'src\NetHop.ShellExtension'
$platform = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
if (-not $Publisher) { $Publisher = 'CN=NetHop, OID.2.25.311729368913984317654407730594956997722=1' }

# 1-2. The handler DLL, with the Visual Studio C++ tools.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
# Ask for the MSVC compiler itself: Visual Studio without it still has MSBuild and the C++ targets, and then fails
# with "MSB8003: The PlatformToolset property is not defined".
$compiler = if ($Architecture -eq 'arm64') { 'Microsoft.VisualStudio.Component.VC.Tools.ARM64' } else { 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64' }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild $compiler -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) {
    throw "Visual Studio with the MSVC C++ build tools for $Architecture was not found (Visual Studio Installer: Desktop development with C++)."
}
& $msbuild (Join-Path $source 'NetHop.ShellExtension.vcxproj') -p:Configuration=Release -p:Platform=$platform -nologo -verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "The context menu handler failed to build for $Architecture." }
Copy-Item (Join-Path $source "bin\$platform\NetHop.ShellExtension.dll") $BinDirectory

# 3. The sparse package: manifest and logos only.
$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
    Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe (Windows SDK) was not found.' }

$layout = Join-Path ([IO.Path]::GetTempPath()) "NetHop.ContextMenu-$Architecture"
Remove-Item $layout -Recurse -Force -ErrorAction SilentlyContinue
New-Item (Join-Path $layout 'Assets') -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $source 'Assets\*.png') (Join-Path $layout 'Assets')
$manifest = (Get-Content (Join-Path $source 'AppxManifest.xml') -Raw).
    Replace('$(Publisher)', [Security.SecurityElement]::Escape($Publisher)).
    Replace('$(Version)', "$Version.0").
    Replace('$(Architecture)', $Architecture)
Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding utf8

$package = Join-Path $BinDirectory 'NetHop.ContextMenu.msix'
& $makeappx.FullName pack /d $layout /p $package /nv /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed for $Architecture." }
Remove-Item $layout -Recurse -Force
Write-Host "$Architecture context menu: $package ($Publisher)"
