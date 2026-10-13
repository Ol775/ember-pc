#Requires -Version 5.1
# Signs files with an Authenticode code-signing certificate and a timestamp, then checks each signature.
#
# The certificate comes from the environment, never from the repository:
#   EMBER_SIGN_PFX           path to a .pfx file, or the .pfx file itself as base64 text
#   EMBER_SIGN_PASSWORD      its password
#   EMBER_SIGN_TIMESTAMP_URL optional RFC 3161 timestamp server (default: http://timestamp.digicert.com)
# Without EMBER_SIGN_PFX the script does nothing and says so, so unsigned local builds still work.
[CmdletBinding()]
param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Path)
$ErrorActionPreference = 'Stop'

if (-not $env:EMBER_SIGN_PFX) { Write-Host 'No signing certificate configured (EMBER_SIGN_PFX): files left unsigned.'; return }

$signtool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw 'signtool.exe was not found. Install the Windows SDK (Signing Tools).' }

$temp = $null
try {
    $pfx = $env:EMBER_SIGN_PFX
    if (-not (Test-Path -LiteralPath $pfx -ErrorAction SilentlyContinue)) {
        $temp = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N') + '.pfx')
        [IO.File]::WriteAllBytes($temp, [Convert]::FromBase64String($pfx))
        $pfx = $temp
    }
    $stamp = if ($env:EMBER_SIGN_TIMESTAMP_URL) { $env:EMBER_SIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
    foreach ($file in $Path) {
        & $signtool.FullName sign /f $pfx /p $env:EMBER_SIGN_PASSWORD /fd SHA256 /tr $stamp /td SHA256 /d 'Ember' $file
        if ($LASTEXITCODE -ne 0) { throw "signtool failed on $file." }
        $sig = Get-AuthenticodeSignature -LiteralPath $file
        if ($sig.Status -ne 'Valid') { throw "$file is not validly signed ($($sig.Status))." }
        Write-Host ("Signed {0} by {1}" -f (Split-Path $file -Leaf), $sig.SignerCertificate.Subject)
    }
}
finally { if ($temp) { Remove-Item $temp -Force -ErrorAction SilentlyContinue } }
