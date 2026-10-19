@{
    RootModule        = 'NetHop.psm1'
    ModuleVersion     = '0.1.0'
    GUID              = '5d1c8f3e-7a2b-4c9d-8e6f-1a2b3c4d5e6f'
    Author            = 'agateeno'
    Copyright         = '(c) 2026 agateeno. MIT License.'
    Description       = 'Switch NetHop network profiles from PowerShell and Power Automate Desktop.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Get-NetHopProfile'
        'Get-NetHopStatus'
        'Set-NetHopProfile'
        'Switch-NetHopProfile'
        'Get-NetHopAdapter'
        'Suspend-NetHopAutomation'
        'Resume-NetHopAutomation'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
    PrivateData       = @{
        PSData = @{
            Tags       = @('network', 'profile', 'ip', 'wifi', 'PowerAutomate')
            LicenseUri = 'https://github.com/agateeno/net-hop/blob/main/LICENSE'
            ProjectUri = 'https://github.com/agateeno/net-hop'
        }
    }
}
