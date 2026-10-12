#Requires -Version 5.1
# Installs the Ember watchdog: a second Windows service that hands the fan headers back to the board's own control
# if the Ember service stops or hangs. Same safety checks as install-service.ps1.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExePath,
    [switch]$AllowInsecurePath
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'install-service.ps1') -ExePath $ExePath -AllowInsecurePath:$AllowInsecurePath `
    -ServiceName 'EmberWatchdog' -DisplayName 'Ember watchdog'
