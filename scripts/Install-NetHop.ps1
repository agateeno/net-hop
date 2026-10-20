#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Quick development install of NetHop from a CI build folder, without the MSI.

.DESCRIPTION
    Copies the build to %ProgramFiles%\NetHop, registers and starts the NetHop Windows service,
    and adds the folder to the machine PATH. Run it from the extracted NetHop-win-x64 artifact.
    For a normal install use the MSI from the same artifact.

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

Get-Process NetHop.App -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Copying files to $Destination..."
New-Item $Destination -ItemType Directory -Force | Out-Null
# The service runs as SYSTEM from this folder: only administrators may change it, as in Program Files.
# A folder elsewhere (C:\NetHop, for example) would otherwise inherit "Users: Modify" and let anyone swap the binaries.
# Set before copying, so the files inherit it.
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
$inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
foreach ($rule in @(@('S-1-5-18', 'FullControl'), @('S-1-5-32-544', 'FullControl'), @('S-1-5-32-545', 'ReadAndExecute'))) {
    # SYSTEM, Administrators, Users
    $sid = New-Object System.Security.Principal.SecurityIdentifier($rule[0])
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sid, $rule[1], $inherit, 'None', 'Allow')))
}
$acl.SetOwner((New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')))
Set-Acl -Path $Destination -AclObject $acl

Copy-Item (Join-Path $Source 'bin\*') $Destination -Recurse -Force

$serviceExe = Join-Path $Destination 'NetHop.Service.exe'
if (-not (Get-Service $serviceName -ErrorAction SilentlyContinue)) {
    Write-Host 'Registering the NetHop service...'
    New-Service -Name $serviceName -BinaryPathName "`"$serviceExe`"" -DisplayName 'NetHop' `
        -Description 'Applies NetHop network profiles and runs NetHop automations.' -StartupType Automatic | Out-Null
    # Restart automatically if it crashes.
    sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}

Start-Service $serviceName
Write-Host 'Service started.'

$cliPath = $Destination
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
if (($machinePath -split ';') -notcontains $cliPath) {
    [Environment]::SetEnvironmentVariable('Path', "$machinePath;$cliPath", 'Machine')
    Write-Host "Added $cliPath to PATH (open a new terminal to use 'nethop')."
}

$moduleSource = Join-Path $Source 'powershell'
if (Test-Path $moduleSource) {
    $moduleTarget = Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules\NetHop'
    New-Item $moduleTarget -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $moduleSource '*') $moduleTarget -Force
    Write-Host 'Installed the NetHop PowerShell module.'
}

Write-Host ''
Write-Host "Done. Start the app with: & '$(Join-Path $Destination 'NetHop.App.exe')'"
Write-Host "Try the CLI with:         nethop adapters"
