#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExePath
)
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'install-service.ps1 must be run from an elevated (Administrator) PowerShell.'
}
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "ExePath does not exist: $ExePath"
}
$exe = (Resolve-Path -LiteralPath $ExePath).ProviderPath

& sc.exe query EmberService *> $null
if ($LASTEXITCODE -eq 0) {
    throw 'EmberService already exists. Run uninstall-service.ps1 first.'
}

# sc.exe needs "binPath=" and its value as separate arguments; the inner quotes protect paths with spaces.
& sc.exe create EmberService binPath= "`"$exe`"" start= auto
if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed (exit $LASTEXITCODE)." }

& sc.exe failure EmberService reset= 86400 actions= restart/5000/restart/5000/restart/30000
if ($LASTEXITCODE -ne 0) { throw "sc.exe failure failed (exit $LASTEXITCODE). The service was created; remove it with uninstall-service.ps1." }

& sc.exe start EmberService
if ($LASTEXITCODE -ne 0) { throw "sc.exe start failed (exit $LASTEXITCODE). The service was created; check the Windows Event Log." }
