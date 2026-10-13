#Requires -Version 5.1
<#
.SYNOPSIS
Installs (or upgrades) Ember from an unzipped release folder: the fan-control service, the watchdog service, the app,
and a Start Menu shortcut. Run from an elevated PowerShell, inside the unzipped folder: scripts\install-ember.ps1

.DESCRIPTION
Everything goes under -InstallDir (default C:\Program Files\Ember), where only administrators can write. That is required:
the services run as LocalSystem, and install-service.ps1 refuses a folder that normal users can change.
On an upgrade the services are stopped, the files replaced and the services started again; settings in
%ProgramData%\Ember and %AppData%\Ember are kept. PawnIO (the signed driver for motherboard sensors) is installed through
install-pawnio.ps1 unless -SkipPawnIO is given.
#>
[CmdletBinding()]
param(
    [string]$SourceDir,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Ember'),
    [switch]$SkipPawnIO
)
$ErrorActionPreference = 'Stop'
if (-not $SourceDir) { $SourceDir = Split-Path $PSScriptRoot -Parent }

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'install-ember.ps1 must be run from an elevated (Administrator) PowerShell.'
}
foreach ($d in 'service', 'watchdog', 'app') {
    if (-not (Test-Path (Join-Path $SourceDir "$d\*"))) { throw "Missing $d in $SourceDir. Run this from an unzipped Ember release (see scripts\build-release.ps1)." }
}
$svcExe = Join-Path $InstallDir 'Ember.Service.exe'
$wdExe = Join-Path $InstallDir 'Watchdog\Ember.Watchdog.exe'
$appExe = Join-Path $InstallDir 'App\Ember.App.exe'

function Test-Service([string]$name) { & sc.exe query $name *> $null; return ($LASTEXITCODE -eq 0) }

# Stop the app and the services (watchdog first, so it does not restart the service we are replacing).
Get-Process -Name 'Ember.App' -ErrorAction SilentlyContinue | Stop-Process -Force
foreach ($n in 'EmberWatchdog', 'EmberService') { if (Test-Service $n) { Stop-Service $n -ErrorAction SilentlyContinue } }
Start-Sleep -Seconds 2

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item (Join-Path $SourceDir 'service\*') $InstallDir -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $InstallDir 'Watchdog') -Force | Out-Null
Copy-Item (Join-Path $SourceDir 'watchdog\*') (Join-Path $InstallDir 'Watchdog') -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $InstallDir 'App') -Force | Out-Null
Copy-Item (Join-Path $SourceDir 'app\*') (Join-Path $InstallDir 'App') -Recurse -Force

if (-not $SkipPawnIO) {
    try { & (Join-Path $PSScriptRoot 'install-pawnio.ps1') }
    catch { Write-Warning "PawnIO was not installed: $($_.Exception.Message). Motherboard sensors and fan headers need it; run scripts\install-pawnio.ps1 later." }
}

# New install: create the services. Upgrade: they already exist, just start them again.
if (-not (Test-Service 'EmberService')) { & (Join-Path $PSScriptRoot 'install-service.ps1') -ExePath $svcExe }
else { Start-Service 'EmberService' }
if (-not (Test-Service 'EmberWatchdog')) { & (Join-Path $PSScriptRoot 'install-watchdog.ps1') -ExePath $wdExe }
else { Start-Service 'EmberWatchdog' }

# Start Menu shortcut for every user.
$menu = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Ember.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($menu)
$link.TargetPath = $appExe
$link.WorkingDirectory = Split-Path $appExe
$link.Description = 'Ember: monitor, cool and light your PC'
$link.IconLocation = "$appExe,0"
$link.Save()

Write-Host "Ember is installed in $InstallDir."
Write-Host 'Open it from the Start Menu. On the Devices page, test one fan header, then enable it.'
Write-Host 'Close any other fan-control program (NZXT CAM, MSI Center, Fan Control, iCUE) first.'
