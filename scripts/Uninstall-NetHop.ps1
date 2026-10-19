#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes a development install made with Install-NetHop.ps1.

.PARAMETER RemoveData
    Also delete %ProgramData%\NetHop (profiles and rules). Without it they are kept.
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $env:ProgramFiles 'NetHop'),
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$serviceName = 'NetHop'

Get-Process NetHop -ErrorAction SilentlyContinue | Stop-Process -Force

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Write-Host 'Service removed.'
}

$cliPath = Join-Path $Destination 'cli'
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
$newPath = ($machinePath -split ';' | Where-Object { $_ -and $_ -ne $cliPath }) -join ';'
[Environment]::SetEnvironmentVariable('Path', $newPath, 'Machine')

if (Test-Path $Destination) {
    Remove-Item $Destination -Recurse -Force
    Write-Host "Removed $Destination."
}

if ($RemoveData) {
    $data = Join-Path $env:ProgramData 'NetHop'
    if (Test-Path $data) {
        Remove-Item $data -Recurse -Force
        Write-Host "Removed $data."
    }
}
