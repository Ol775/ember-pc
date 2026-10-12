#Requires -Version 5.1
# Downloads the pinned OpenRGB release into third-party/openrgb, where the app build picks it up and ships it in an "OpenRGB"
# folder next to Ember.App.exe. OpenRGB is GPL-2.0 software that Ember runs as a separate program and talks to over its
# network SDK on this PC; see THIRD-PARTY.md. The download is checked against a pinned SHA-256 before it is unpacked.
[CmdletBinding()]
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$url = 'https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.0/OpenRGB_1.0_Windows_64_81bbe18.zip'
$sha256 = '182A52A3C97C4C4AE52C80286B4260C9666C51C3447DBC416EE8945F66192E90'
$dest = Join-Path (Split-Path $PSScriptRoot -Parent) 'third-party\openrgb'

if ((Test-Path (Join-Path $dest 'OpenRGB.exe')) -and -not $Force) { Write-Host "OpenRGB is already in $dest"; return }

$work = Join-Path ([IO.Path]::GetTempPath()) ('ember-openrgb-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $zip = Join-Path $work 'openrgb.zip'
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($actual -ne $sha256) { throw "The download does not match the pinned hash (got $actual). Nothing was installed." }
    $unpacked = Join-Path $work 'unpacked'
    Expand-Archive $zip $unpacked
    $exe = Get-ChildItem $unpacked -Recurse -Filter OpenRGB.exe | Select-Object -First 1
    if (-not $exe) { throw 'OpenRGB.exe was not in the download.' }
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Path $dest | Out-Null
    Copy-Item (Join-Path $exe.DirectoryName '*') $dest -Recurse
    Write-Host "OpenRGB 1.0 is ready in $dest"
}
finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
