#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Development install of NetHop from a CI build folder (until a real installer exists).

.DESCRIPTION
    Copies the build to %ProgramFiles%\NetHop, registers and starts the NetHop Windows service,
    and adds the CLI folder to the machine PATH. Run it from the extracted NetHop-win-x64 artifact.

.EXAMPLE
    .\Install-NetHop.ps1
#>
[CmdletBinding()]
param(
    [string]$Source = $PSScriptRoot,
    [string]$Destination = (Join-Path $env:ProgramFiles 'NetHop')
)

$ErrorActionPreference = 'Stop'
$serviceName = 'NetHop'

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Write-Host 'Stopping existing service...'
    Stop-Service $serviceName -Force
    # Wait for the process to release its files.
    Start-Sleep -Seconds 2
}

Write-Host "Copying files to $Destination..."
foreach ($part in 'service', 'cli', 'app') {
    $target = Join-Path $Destination $part
    New-Item $target -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $Source "$part\*") $target -Recurse -Force
}

$serviceExe = Join-Path $Destination 'service\NetHop.Service.exe'
if (-not (Get-Service $serviceName -ErrorAction SilentlyContinue)) {
    Write-Host 'Registering the NetHop service...'
    New-Service -Name $serviceName -BinaryPathName "`"$serviceExe`"" -DisplayName 'NetHop' `
        -Description 'Applies NetHop network profiles and runs NetHop automations.' -StartupType Automatic | Out-Null
    # Restart automatically if it crashes.
    sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}

Start-Service $serviceName
Write-Host 'Service started.'

$cliPath = Join-Path $Destination 'cli'
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
if (($machinePath -split ';') -notcontains $cliPath) {
    [Environment]::SetEnvironmentVariable('Path', "$machinePath;$cliPath", 'Machine')
    Write-Host "Added $cliPath to PATH (open a new terminal to use 'nethop')."
}

Write-Host ''
Write-Host "Done. Start the app with: & '$(Join-Path $Destination 'app\NetHop.exe')'"
Write-Host "Try the CLI with:         nethop adapters"
