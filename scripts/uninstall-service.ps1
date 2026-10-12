#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ServiceName = 'EmberService'
)
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'uninstall-service.ps1 must be run from an elevated (Administrator) PowerShell.'
}

& sc.exe query $ServiceName *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "$ServiceName is not installed; nothing to do."
    return
}

# Waits for the stop; harmless if already stopped.
Stop-Service $ServiceName -ErrorAction SilentlyContinue

& sc.exe delete $ServiceName
if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed (exit $LASTEXITCODE)." }
