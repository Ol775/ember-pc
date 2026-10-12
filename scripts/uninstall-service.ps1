#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'uninstall-service.ps1 must be run from an elevated (Administrator) PowerShell.'
}

& sc.exe query EmberService *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'EmberService is not installed; nothing to do.'
    return
}

# Stop may fail if the service is already stopped; that is fine.
& sc.exe stop EmberService *> $null

& sc.exe delete EmberService
if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed (exit $LASTEXITCODE)." }
