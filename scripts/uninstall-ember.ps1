#Requires -Version 5.1
# Removes the Ember services, the Start Menu shortcut and (with -RemoveFiles) the program files.
# Your settings in %ProgramData%\Ember and %AppData%\Ember are left alone. Fans stay at their last safe speed until you
# restart the PC, which returns the board to automatic control. PawnIO is a separate driver and is not removed.
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Ember'),
    [switch]$RemoveFiles
)
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'uninstall-ember.ps1 must be run from an elevated (Administrator) PowerShell.'
}
Get-Process -Name 'Ember.App' -ErrorAction SilentlyContinue | Stop-Process -Force
& (Join-Path $PSScriptRoot 'uninstall-watchdog.ps1')
& (Join-Path $PSScriptRoot 'uninstall-service.ps1')
$menu = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Ember.lnk'
if (Test-Path $menu) { Remove-Item $menu -Force }
if ($RemoveFiles -and (Test-Path $InstallDir)) { Start-Sleep -Seconds 2; Remove-Item $InstallDir -Recurse -Force }
Write-Host 'Ember has been removed.'
