#Requires -Version 5.1
# Builds a release: dist\Ember-<version>\{app,service,watchdog,scripts}, the portable zip dist\Ember-<version>.zip, and the
# interactive installer dist\Ember-Setup-<version>.exe (terms, options, the Microsoft runtimes and Ember inside one file).
# Portable: unzip on the target PC and run scripts\install-ember.ps1 from an elevated PowerShell (needs the .NET 8 Desktop Runtime).
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipOpenRgb,
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not $SkipOpenRgb) { & (Join-Path $PSScriptRoot 'fetch-openrgb.ps1') }
    if (-not $SkipInstaller) { & (Join-Path $PSScriptRoot 'fetch-prereqs.ps1') }

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
    # With a signing certificate in the environment (see sign-files.ps1) Ember's own programs are signed before they are packed.
    $own = Get-ChildItem $out -Recurse -Include 'Ember.App.exe', 'Ember.Service.exe', 'Ember.Watchdog.exe' | ForEach-Object FullName
    & (Join-Path $PSScriptRoot 'sign-files.ps1') @own
    foreach ($doc in 'LICENSE', 'TERMS.md', 'PRIVACY.md', 'THIRD-PARTY.md', 'SECURITY.md', 'CHANGELOG.md', 'README.md') { Copy-Item (Join-Path $root $doc) $out }

    $zip = "$out.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash
    Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ascii
    Write-Host "Built $zip"
    Write-Host "SHA-256 $hash"

    if (-not $SkipInstaller) {
        # The installer carries the zip above, plus Microsoft's runtime installers from third-party\prereqs.
        & dotnet build "src/Ember.Setup" -c $Configuration --no-incremental "-p:PayloadZip=$zip"
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build Ember.Setup failed.' }
        $setup = Join-Path $root 'src\Ember.Setup\bin\$Configuration\net48\EmberSetup.exe'
        $setup = $setup.Replace('$Configuration', $Configuration)
        $setupOut = Join-Path $root "dist\Ember-Setup-$version.exe"
        Copy-Item $setup $setupOut -Force
        & (Join-Path $PSScriptRoot 'sign-files.ps1') $setupOut
        $setupHash = (Get-FileHash $setupOut -Algorithm SHA256).Hash
        Set-Content -Path "$setupOut.sha256" -Value "$setupHash  $(Split-Path $setupOut -Leaf)" -Encoding ascii
        Write-Host ("Built {0} ({1:N1} MB)" -f $setupOut, ((Get-Item $setupOut).Length / 1MB))
        Write-Host "SHA-256 $setupHash"
    }
}
finally { Pop-Location }
