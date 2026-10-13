#Requires -Version 5.1
# Builds a release folder and zip: dist\Ember-<version>\{app,service,watchdog,scripts} and dist\Ember-<version>.zip.
# Unzip it on the target PC and run scripts\install-ember.ps1 from an elevated PowerShell.
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipOpenRgb
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not $SkipOpenRgb) { & (Join-Path $PSScriptRoot 'fetch-openrgb.ps1') }

    $version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
    $out = Join-Path $root "dist\Ember-$version"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }

    foreach ($p in @(@('Ember.Service', 'service'), @('Ember.Watchdog', 'watchdog'), @('Ember.App', 'app'))) {
        & dotnet publish "src/$($p[0])" -c $Configuration -r win-x64 --self-contained false -o (Join-Path $out $p[1])
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish $($p[0]) failed." }
    }
    $scripts = Join-Path $out 'scripts'
    New-Item -ItemType Directory -Path $scripts | Out-Null
    foreach ($s in 'install-ember.ps1', 'uninstall-ember.ps1', 'install-service.ps1', 'install-watchdog.ps1', 'uninstall-service.ps1', 'uninstall-watchdog.ps1', 'install-pawnio.ps1') {
        Copy-Item (Join-Path $PSScriptRoot $s) $scripts
    }
    Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY.md'), (Join-Path $root 'README.md') $out

    $zip = "$out.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
    Write-Host "Built $zip"
    Write-Host "SHA-256 $hash"
}
finally { Pop-Location }
