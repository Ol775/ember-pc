#Requires -Version 5.1
# Downloads Corsair's two SDK releases into third-party/icue, where the app build picks up the libraries from each SDK's own
# "redist" folder and ships them, unmodified, in an "iCUE" folder next to Ember.App.exe:
#   - the iCUE SDK (version 4), which newer iCUE installs accept, and
#   - the older CUE SDK (version 3), which Ember falls back to when iCUE refuses the newer one.
# Each download is checked against a pinned SHA-256 before it is unpacked. Ember uses them to talk to the Corsair iCUE program on the PC.
[CmdletBinding()]
param([switch]$Force)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$dest = Join-Path (Split-Path $PSScriptRoot -Parent) 'third-party\icue'
$items = @(
    @{ File = 'iCUESDK.x64_2019.dll'; Label = 'iCUE SDK 4.0.84'
       Url = 'https://github.com/CorsairOfficial/cue-sdk/releases/download/v4.0.84/iCUESDK_4.0.84.zip'
       Sha256 = 'EB4414CF505145F3E507DC839AD587BF7CA684D64BE3D9A834F416A136736D5D' },
    @{ File = 'CUESDK.x64_2019.dll'; Label = 'CUE SDK 3.0.464'
       Url = 'https://github.com/CorsairOfficial/cue-sdk/releases/download/v3.0.464/CUESDK_3.0.464.zip'
       Sha256 = '5285843968841D5D75E2C4B9399AAE59262EED999DD0F269FD176A5022E521BC' }
)
New-Item -ItemType Directory -Path $dest -Force | Out-Null

foreach ($i in $items) {
    if ((Test-Path (Join-Path $dest $i.File)) -and -not $Force) { Write-Host "$($i.Label) is already in $dest"; continue }
    $work = Join-Path ([IO.Path]::GetTempPath()) ('ember-icue-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $zip = Join-Path $work 'sdk.zip'
        Invoke-WebRequest $i.Url -OutFile $zip -UseBasicParsing
        $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
        if ($actual -ne $i.Sha256) { throw "The $($i.Label) download does not match the pinned hash (got $actual). Nothing was installed." }
        $unpacked = Join-Path $work 'unpacked'
        Expand-Archive $zip $unpacked
        $dll = Get-ChildItem $unpacked -Recurse -Filter $i.File | Select-Object -First 1
        if (-not $dll) { throw "$($i.File) was not in the $($i.Label) download." }
        Copy-Item $dll.FullName $dest -Force
        Write-Host "$($i.Label) is ready in $dest"
    }
    finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
}
