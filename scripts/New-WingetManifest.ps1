<#
.SYNOPSIS
    Writes the winget manifests for a NetHop release.

.DESCRIPTION
    Reads each MSI's SHA256 and ProductCode and writes the four manifest files (version, installer,
    en-US and it-IT locales) under manifests\a\agateeno\NetHop\<version>. Submit them to
    https://github.com/microsoft/winget-pkgs (for example with "wingetcreate submit <folder>").
    The MSIs must already be published at -ReleaseUrl.

.EXAMPLE
    .\New-WingetManifest.ps1 -Version 0.2.0 -Msi .\NetHop-0.2.0-x64.msi, .\NetHop-0.2.0-arm64.msi `
        -ReleaseUrl https://github.com/agateeno/net-hop/releases/download/v0.2.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string[]]$Msi,
    [Parameter(Mandatory = $true)][string]$ReleaseUrl,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\out\winget')
)

$ErrorActionPreference = 'Stop'
$id = 'agateeno.NetHop'
$schema = '1.9.0'
$folder = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) "manifests\a\agateeno\NetHop\$Version"
New-Item $folder -ItemType Directory -Force | Out-Null

function Get-MsiProperty([string]$path, [string]$name) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @((Resolve-Path $path).Path, 0))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @("SELECT Value FROM Property WHERE Property = '$name'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    $value = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return $value
}

$installers = foreach ($file in $Msi) {
    $name = Split-Path $file -Leaf
    $arch = if ($name -match '-(x64|arm64)\.msi$') { $Matches[1] } else { throw "Cannot tell the architecture of $name (expected -x64.msi or -arm64.msi)." }
    @"
  - Architecture: $arch
    InstallerUrl: $ReleaseUrl/$name
    InstallerSha256: $((Get-FileHash $file -Algorithm SHA256).Hash)
    ProductCode: '$(Get-MsiProperty $file 'ProductCode')'
"@
}

# Single-quoted YAML scalar: safe for ': ', quotes and apostrophes.
function Q([string]$text) { "'" + $text.Replace("'", "''") + "'" }

$header = "# yaml-language-server: `$schema=https://aka.ms/winget-manifest"

@"
$header.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@ | Set-Content (Join-Path $folder "$id.yaml") -Encoding utf8

@"
$header.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: wix
Scope: machine
InstallModes:
  - interactive
  - silent
UpgradeBehavior: install
FileExtensions:
  - nethop
Protocols:
  - nethop
Commands:
  - nethop
Installers:
$($installers -join "`n")
ManifestType: installer
ManifestVersion: $schema
"@ | Set-Content (Join-Path $folder "$id.installer.yaml") -Encoding utf8

$locales = @{
    'en-US' = @{
        Short = 'Switch network profiles: adapters, static or automatic IPv4/IPv6, gateways and DNS.'
        Description = 'NetHop is a network profile switcher for Windows 11 with a Fluent tray flyout, built-in automations (for example "when I join this Wi-Fi, apply that profile"), a CLI, a PowerShell module, Power Automate actions and a Logi Actions plugin.'
        Tags = @('network', 'ip', 'dhcp', 'dns', 'wifi', 'profile')
    }
    'it-IT' = @{
        Short = 'Cambia profilo di rete: schede, IPv4/IPv6 statico o automatico, gateway e DNS.'
        Description = 'NetHop è un gestore di profili di rete per Windows 11 con un pannello Fluent nell''area di notifica, automazioni integrate (ad esempio "quando mi collego a questo Wi-Fi, applica quel profilo"), una CLI, un modulo PowerShell, azioni per Power Automate e un plugin Logi Actions.'
        Tags = @('rete', 'ip', 'dhcp', 'dns', 'wifi', 'profilo')
    }
}

foreach ($locale in $locales.Keys) {
    $data = $locales[$locale]
    $type = if ($locale -eq 'en-US') { 'defaultLocale' } else { 'locale' }
    $common = if ($locale -eq 'en-US') {
        @"
Publisher: agateeno
PublisherUrl: https://github.com/agateeno
PackageName: NetHop
PackageUrl: https://github.com/agateeno/net-hop
License: MIT
LicenseUrl: https://github.com/agateeno/net-hop/blob/main/LICENSE
ReleaseNotesUrl: https://github.com/agateeno/net-hop/releases/tag/v$Version
"@
    } else { 'PackageName: NetHop' }

    @"
$header.$type.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: $locale
$common
ShortDescription: $(Q $data.Short)
Description: $(Q $data.Description)
Tags:
$(($data.Tags | ForEach-Object { "  - $_" }) -join "`n")
ManifestType: $type
ManifestVersion: $schema
"@ | Set-Content (Join-Path $folder "$id.locale.$locale.yaml") -Encoding utf8
}

Write-Host "Manifests written to $folder"
