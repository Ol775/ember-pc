#Requires -Version 5.1
# Removes Ember: the services, shortcuts, the "Installed apps" entry and (with -RemoveFiles) the program files.
# Your settings in %ProgramData%\Ember and %AppData%\Ember are kept unless you add -RemoveSettings. Fans stay at their last safe
# speed until you restart the PC, which returns the board to automatic control. The PawnIO driver and the Microsoft runtimes
# are separate and are not removed, because other programs may use them.
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Ember'),
    [switch]$RemoveFiles,
    [switch]$RemoveSettings,
    # Show a message box when finished (used when started from "Installed apps").
    [switch]$ShowResult
)
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'uninstall-ember.ps1 must be run from an elevated (Administrator) PowerShell.'
}
try {
    Get-Process -Name 'Ember.App' -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID } | Stop-Process -Force
    & (Join-Path $PSScriptRoot 'uninstall-watchdog.ps1')
    & (Join-Path $PSScriptRoot 'uninstall-service.ps1')
    foreach ($dir in 'CommonPrograms', 'CommonDesktopDirectory') {
        $lnk = Join-Path ([Environment]::GetFolderPath($dir)) 'Ember.lnk'
        if (Test-Path $lnk) { Remove-Item $lnk -Force }
    }
    Get-NetFirewallRule -DisplayName 'Ember: Govee lamp replies' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
    Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Ember' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item 'HKLM:\SOFTWARE\Ember' -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Ember' -ErrorAction SilentlyContinue
    if ($RemoveFiles -and (Test-Path $InstallDir)) { Start-Sleep -Seconds 2; Remove-Item $InstallDir -Recurse -Force }
    if ($RemoveSettings) {
        foreach ($p in (Join-Path $env:ProgramData 'Ember'), (Join-Path $env:APPDATA 'Ember')) { if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
    }
    $message = 'Ember has been removed.' + $(if ($RemoveSettings) { '' } else { ' Your settings were kept.' })
}
catch { $message = "Ember could not be removed completely: $($_.Exception.Message)" }
Write-Host $message
if ($ShowResult) {
    Add-Type -AssemblyName PresentationFramework
    [void][System.Windows.MessageBox]::Show($message, 'Ember')
}
