#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'uninstall-service.ps1') -ServiceName 'EmberWatchdog'
