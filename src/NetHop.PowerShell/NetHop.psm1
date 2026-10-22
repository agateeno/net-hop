# NetHop PowerShell module: a thin wrapper around nethop.exe --json.
# Works in Windows PowerShell 5.1 (used by Power Automate Desktop) and PowerShell 7.

Set-StrictMode -Version 3.0

function Get-NetHopCli {
    if ($env:NETHOP_CLI -and (Test-Path $env:NETHOP_CLI)) { return $env:NETHOP_CLI }

    $installed = Join-Path $env:ProgramFiles 'NetHop\nethop.exe'
    if (Test-Path $installed) { return $installed }

    $onPath = Get-Command nethop.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    throw 'nethop.exe was not found. Install NetHop or set the NETHOP_CLI environment variable.'
}

function Invoke-NetHopCli {
    param([string[]]$Arguments, [switch]$AllowFailure)

    $cli = Get-NetHopCli
    $output = & $cli @Arguments --json 2>&1
    $code = $LASTEXITCODE
    $stdout = ($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] }) -join "`n"
    $stderr = ($output | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] }) -join "`n"

    switch ($code) {
        0 { }
        1 { if (-not $AllowFailure) { throw "NetHop: $stderr$stdout" } }
        2 { throw "NetHop usage error: $stderr" }
        3 { throw 'The NetHop service is not running. Turn it on in NetHop > Settings > NetHop service.' }
        4 { throw 'NetHop requires an administrator account.' }
        default { throw "nethop.exe exited with code ${code}: $stderr" }
    }

    if ($stdout.Trim().Length -eq 0) { return $null }
    return $stdout | ConvertFrom-Json
}

<#
.SYNOPSIS
    Lists NetHop profiles.
.EXAMPLE
    Get-NetHopProfile | Where-Object isActive
#>
function Get-NetHopProfile {
    [CmdletBinding()]
    param([Parameter(Position = 0)][string]$Name)

    $profiles = @(Invoke-NetHopCli -Arguments @('list'))
    if ($Name) { $profiles = @($profiles | Where-Object { $_.name -like $Name }) }
    $profiles
}

<#
.SYNOPSIS
    Shows the active profile and the automation state.
#>
function Get-NetHopStatus {
    [CmdletBinding()]
    param()
    Invoke-NetHopCli -Arguments @('status')
}

<#
.SYNOPSIS
    Applies a profile by name or id.
.PARAMETER WhatIf
    Shows the steps without changing anything.
.EXAMPLE
    Set-NetHopProfile Office
.EXAMPLE
    Get-NetHopProfile Home | Set-NetHopProfile
#>
function Set-NetHopProfile {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true, Position = 0, ValueFromPipelineByPropertyName = $true)]
        [Alias('Profile', 'Id')]
        [string]$Name
    )
    process {
        $dryRun = -not $PSCmdlet.ShouldProcess($Name, 'Apply NetHop profile')
        $arguments = @('apply', $Name)
        if ($dryRun) { $arguments += '--dry-run' }

        $result = Invoke-NetHopCli -Arguments $arguments -AllowFailure
        if (-not $result.success) {
            $message = "Profile '$Name' failed: $($result.error)"
            if ($result.rolledBack) { $message += ' The previous configuration was restored.' }
            throw $message
        }
        $result
    }
}

<#
.SYNOPSIS
    Lists network adapters with their live configuration.
#>
function Get-NetHopAdapter {
    [CmdletBinding()]
    param([Parameter(Position = 0)][string]$Name)

    $adapters = @((Invoke-NetHopCli -Arguments @('adapters')).adapters)
    if ($Name) { $adapters = @($adapters | Where-Object { $_.info.name -like $Name }) }
    $adapters
}

<#
.SYNOPSIS
    Pauses NetHop automations.
.PARAMETER Minutes
    How long to pause. Defaults to 60.
#>
function Suspend-NetHopAutomation {
    [CmdletBinding()]
    param([ValidateRange(1, 1440)][int]$Minutes = 60)
    $null = & (Get-NetHopCli) pause $Minutes
    if ($LASTEXITCODE -ne 0) { throw "nethop.exe exited with code $LASTEXITCODE" }
}

<#
.SYNOPSIS
    Resumes NetHop automations.
#>
function Resume-NetHopAutomation {
    [CmdletBinding()]
    param()
    $null = & (Get-NetHopCli) resume
    if ($LASTEXITCODE -ne 0) { throw "nethop.exe exited with code $LASTEXITCODE" }
}

Export-ModuleMember -Function Get-NetHopProfile, Get-NetHopStatus, Set-NetHopProfile,
    Get-NetHopAdapter, Suspend-NetHopAutomation, Resume-NetHopAutomation
