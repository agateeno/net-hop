<#
.SYNOPSIS
    Builds, signs and packs the NetHop custom actions for Power Automate Desktop.

.DESCRIPTION
    Power Automate only loads custom actions whose DLLs and .cab are signed with a certificate that
    the machines running the flows trust (Trusted Root Certification Authorities). This script:
      1. builds src\NetHop.PowerAutomate in Release,
      2. signs Modules.NetHop.dll,
      3. packs it into NetHop.cab with makecab (the SDK DLL is not included: Power Automate provides it),
      4. signs the .cab.
    Upload the .cab in Power Automate (portal > Custom actions > Upload custom action).

.PARAMETER CertificatePath
    A .pfx code-signing certificate with an exportable private key.

.PARAMETER CertificateThumbprint
    Alternatively, the thumbprint of a code-signing certificate in Cert:\CurrentUser\My.

.PARAMETER SkipSigning
    Build and pack without signing (the .cab will not be usable in flows; for checking the build only).

.EXAMPLE
    .\Build-PowerAutomateModule.ps1 -CertificatePath .\codesign.pfx
#>
[CmdletBinding(DefaultParameterSetName = 'Pfx')]
param(
    [Parameter(ParameterSetName = 'Pfx', Mandatory = $true)][string]$CertificatePath,
    [Parameter(ParameterSetName = 'Pfx')][securestring]$CertificatePassword,
    [Parameter(ParameterSetName = 'Store', Mandatory = $true)][string]$CertificateThumbprint,
    [Parameter(ParameterSetName = 'Unsigned', Mandatory = $true)][switch]$SkipSigning,
    # Pack an already built (and possibly already signed) bin folder, e.g. in CI.
    [switch]$SkipBuild,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\out\power-automate')
)

$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $repo 'src\NetHop.PowerAutomate\NetHop.PowerAutomate.csproj'
$buildDir = Join-Path $OutputDirectory 'bin'

if (-not $SkipBuild) {
    Write-Host 'Building the module...'
    dotnet build $project -c Release -o $buildDir
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

function Get-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $found = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) { throw 'signtool.exe not found. Install the Windows SDK.' }
    return $found.FullName
}

function Invoke-Sign([string]$file) {
    if ($SkipSigning) { return }
    $signtool = Get-SignTool
    $common = @('sign', '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')
    if ($PSCmdlet.ParameterSetName -eq 'Store') {
        & $signtool @common /sha1 $CertificateThumbprint $file
    } else {
        $arguments = $common + @('/f', (Resolve-Path $CertificatePath))
        if ($CertificatePassword) {
            $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword))
            $arguments += @('/p', $plain)
        }
        & $signtool @arguments $file
    }
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $file" }
}

$moduleDll = Join-Path $buildDir 'Modules.NetHop.dll'
Write-Host 'Signing the module DLL...'
Invoke-Sign $moduleDll

# makecab directive file, as in Microsoft's documentation for custom actions.
$cabName = 'NetHop.cab'
$ddf = @"
.OPTION EXPLICIT
.Set CabinetName1=$cabName
.Set DiskDirectory1=$OutputDirectory
.Set CompressionType=LZX
.Set Cabinet=on
.Set Compress=on
.Set CabinetFileCountThreshold=0
.Set FolderFileCountThreshold=0
.Set FolderSizeThreshold=0
.Set MaxCabinetSize=0
.Set MaxDiskFileCount=0
.Set MaxDiskSize=0
"$moduleDll" "Modules.NetHop.dll"
"@
$ddfPath = Join-Path $env:TEMP 'NetHop.PowerAutomate.ddf'
$ddf | Out-File -Encoding ascii $ddfPath
Push-Location $env:TEMP
try {
    makecab.exe /F $ddfPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'makecab failed.' }
} finally {
    Pop-Location
    Remove-Item $ddfPath -ErrorAction SilentlyContinue
}

$cab = Join-Path $OutputDirectory $cabName
Write-Host 'Signing the .cab...'
Invoke-Sign $cab

Write-Host ''
Write-Host "Done: $cab"
if ($SkipSigning) { Write-Warning 'The .cab is NOT signed: Power Automate will refuse it.' }
