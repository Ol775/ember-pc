#Requires -Version 5.1
# Downloads Corsair's iCUE SDK release into third-party/icue, where the app build picks up the library from the SDK's own
# "redist" folder and ships it, unmodified, in an "iCUE" folder next to Ember.App.exe. The download is checked against a
# pinned SHA-256 before it is unpacked. Ember uses it to talk to the Corsair iCUE program on the PC.
[CmdletBinding()]
param([switch]$Force)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$url = 'https://github.com/CorsairOfficial/cue-sdk/releases/download/v4.0.84/iCUESDK_4.0.84.zip'
$sha256 = 'EB4414CF505145F3E507DC839AD587BF7CA684D64BE3D9A834F416A136736D5D'
$dest = Join-Path (Split-Path $PSScriptRoot -Parent) 'third-party\icue'

if ((Test-Path (Join-Path $dest 'iCUESDK.x64_2019.dll')) -and -not $Force) { Write-Host "The iCUE SDK is already in $dest"; return }

$work = Join-Path ([IO.Path]::GetTempPath()) ('ember-icue-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $zip = Join-Path $work 'icue.zip'
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($actual -ne $sha256) { throw "The download does not match the pinned hash (got $actual). Nothing was installed." }
    $unpacked = Join-Path $work 'unpacked'
    Expand-Archive $zip $unpacked
    $dll = Get-ChildItem $unpacked -Recurse -Filter 'iCUESDK.x64_2019.dll' | Select-Object -First 1
    if (-not $dll) { throw 'iCUESDK.x64_2019.dll was not in the download.' }
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Path $dest | Out-Null
    Copy-Item $dll.FullName $dest
    Write-Host "iCUE SDK 4.0.84 is ready in $dest"
}
finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
