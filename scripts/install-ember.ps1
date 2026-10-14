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
    [switch]$SkipPawnIO,
    # Also put a shortcut on the desktop of every user.
    [switch]$DesktopShortcut,
    # The version of the Terms of Use the person accepted in the installer (0 = none asked here). Recorded for every user.
    [int]$AcceptedTerms = 0
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
# Ember starts OpenRGB from its own folder (App\OpenRGB) and OpenRGB keeps running when Ember is closed. Any program still running
# from the install folder holds its files open and blocks the copy below, so close those too. An OpenRGB the person runs from
# somewhere else is not touched.
$prefix = $InstallDir.TrimEnd('\') + '\'
$holding = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } catch { $false } })
foreach ($p in $holding) { Write-Host "Closing $($p.ProcessName) (running from the Ember folder)."; $p | Stop-Process -Force -ErrorAction SilentlyContinue }
if ($holding) { $holding | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2

# Copy a folder, trying again for a few seconds when a file is still locked (a virus scanner, or a program that was slow to close),
# and say which program holds it when it still fails.
function Copy-WithRetry([string]$from, [string]$to) {
    for ($try = 1; $try -le 5; $try++) {
        try { Copy-Item $from $to -Recurse -Force -ErrorAction Stop; return }
        catch {
            if ($try -eq 5) {
                throw "Could not copy Ember's files to $to ($($_.Exception.Message)). Close any program using files in the Ember folder (OpenRGB, Ember, a file window) and run setup again."
            }
            Write-Host "A file is in use; trying again ($try of 5)..."
            Start-Sleep -Seconds 3
        }
    }
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-WithRetry (Join-Path $SourceDir 'service\*') $InstallDir
New-Item -ItemType Directory -Path (Join-Path $InstallDir 'Watchdog') -Force | Out-Null
Copy-WithRetry (Join-Path $SourceDir 'watchdog\*') (Join-Path $InstallDir 'Watchdog')
New-Item -ItemType Directory -Path (Join-Path $InstallDir 'App') -Force | Out-Null
Copy-WithRetry (Join-Path $SourceDir 'app\*') (Join-Path $InstallDir 'App')

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

# Govee lamps answer Ember's scan on UDP port 4002. Without a rule, Windows asks the first time the app runs, and if that question
# is dismissed it keeps a Block rule for the program, which beats any Allow rule and silently stops lamps from being found.
# So: remove Windows' own rules for this exact program, and allow only that one port, from the local network, on private networks.
try {
    Get-NetFirewallApplicationFilter -ErrorAction Stop | Where-Object { $_.Program -eq $appExe } |
        ForEach-Object { $_ | Get-NetFirewallRule } | Where-Object { $_.Action -eq 'Block' -or $_.DisplayName -eq 'Ember: Govee lamp replies' } |
        Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName 'Ember: Govee lamp replies' -Description 'Lets Govee lamps on your local network answer Ember (UDP 4002).' `
        -Direction Inbound -Action Allow -Program $appExe -Protocol UDP -LocalPort 4002 -RemoteAddress LocalSubnet -Profile Private, Domain | Out-Null
} catch { Write-Warning "The Windows Firewall rule for Govee lamps was not added: $($_.Exception.Message). Lamps may not be found until you allow Ember when Windows asks." }

# Desktop shortcut, only when asked for.
$desktop = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Ember.lnk'
if ($DesktopShortcut) {
    $d = $shell.CreateShortcut($desktop)
    $d.TargetPath = $appExe
    $d.WorkingDirectory = Split-Path $appExe
    $d.Description = 'Ember: monitor, cool and light your PC'
    $d.IconLocation = "$appExe,0"
    $d.Save()
} elseif (Test-Path $desktop) { Remove-Item $desktop -Force }

# Keep the removal scripts and the legal files next to the program: "Installed apps" in Windows Settings runs the uninstall from here.
$scriptsDir = Join-Path $InstallDir 'scripts'
New-Item -ItemType Directory -Path $scriptsDir -Force | Out-Null
if ((Resolve-Path $PSScriptRoot).ProviderPath -ne (Resolve-Path $scriptsDir).ProviderPath) { Copy-Item (Join-Path $PSScriptRoot '*.ps1') $scriptsDir -Force }
foreach ($doc in 'LICENSE', 'TERMS.md', 'PRIVACY.md', 'THIRD-PARTY.md', 'README.md') {
    $src = Join-Path $SourceDir $doc
    if (Test-Path $src) { Copy-Item $src $InstallDir -Force }
}

# Add the program to "Installed apps", and record where it is and which terms were accepted.
$version = (Get-Item $appExe).VersionInfo.ProductVersion -replace '\+.*$', ''
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Ember'
New-Item -Path $uninstallKey -Force | Out-Null
$sizeKb = [int]((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
$entries = [ordered]@{
    DisplayName = 'Ember'; DisplayVersion = $version; Publisher = 'Ol775'; InstallLocation = $InstallDir
    DisplayIcon = "$appExe,0"; UninstallString = "`"$appExe`" --uninstall"; URLInfoAbout = 'https://github.com/Ol775/ember-pc'
}
foreach ($k in $entries.Keys) { Set-ItemProperty -Path $uninstallKey -Name $k -Value $entries[$k] }
Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value $sizeKb -Type DWord
$emberKey = 'HKLM:\SOFTWARE\Ember'
New-Item -Path $emberKey -Force | Out-Null
Set-ItemProperty -Path $emberKey -Name InstallDir -Value $InstallDir
Set-ItemProperty -Path $emberKey -Name Version -Value $version
$before = (Get-ItemProperty -Path $emberKey -Name AcceptedTerms -ErrorAction SilentlyContinue).AcceptedTerms
if ($AcceptedTerms -gt 0 -and $AcceptedTerms -gt [int]$before) { Set-ItemProperty -Path $emberKey -Name AcceptedTerms -Value $AcceptedTerms -Type DWord }

# Check that everything Ember needs is in place, and say plainly what is not. The parts Ember ships (lighting engine, SDK library)
# are in the install folder; the driver and runtimes are installed above; other makers' programs are theirs to install.
Write-Host ''
Write-Host 'What Ember needs on this PC:'
function Show([string]$what, [bool]$ok, [string]$fix) { if ($ok) { Write-Host "  OK       $what" } else { Write-Warning "$what is missing. $fix"; } }
Show 'Ember app and services' ((Test-Path $appExe) -and (Test-Path $svcExe) -and (Test-Path $wdExe)) 'Run setup again.'
Show 'OpenRGB (shipped with Ember, lights motherboard, memory, fans and more)' (Test-Path (Join-Path $InstallDir 'App\OpenRGB\OpenRGB.exe')) 'Run setup again; lighting for OpenRGB devices will not work without it.'
Show 'Corsair iCUE SDK library (shipped with Ember)' (Test-Path (Join-Path $InstallDir 'App\iCUE\iCUESDK.x64_2019.dll')) 'Run setup again; Corsair lighting will not work without it.'
& sc.exe query PawnIO *> $null
Show 'PawnIO driver (motherboard sensors and fan headers)' ($LASTEXITCODE -eq 0) 'Run scripts\install-pawnio.ps1 as administrator, or run setup again with internet access.'
$vc = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue
Show 'Visual C++ runtime (needed by OpenRGB and the iCUE library)' ($vc -and $vc.Installed -eq 1) 'Install it from https://aka.ms/vs/17/release/vc_redist.x64.exe.'
Write-Host '  Optional, installed by their makers if you own the hardware: Logitech G HUB, Corsair iCUE, Razer Synapse, Govee and Hue apps.'
Write-Host ''
Write-Host "Ember is installed in $InstallDir."
Write-Host 'Open it from the Start Menu. On the Devices page, test one fan header, then enable it.'
Write-Host 'Close any other fan-control program (NZXT CAM, MSI Center, Fan Control, iCUE) first.'
