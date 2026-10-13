#Requires -Version 5.1
# Downloads Microsoft's own installers for the two runtimes Ember needs on a PC that does not have them yet, into
# third-party/prereqs, where build-release.ps1 packs them into the Ember installer:
#   - the .NET 8 Desktop Runtime (runs Ember), and
#   - the Visual C++ Redistributable for x64 (needed by OpenRGB).
# Each file must carry a valid Authenticode signature made by Microsoft, or it is deleted and the script stops. The installer
# only runs them when the runtime is missing from the PC.
[CmdletBinding()]
param([switch]$Force)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$dest = Join-Path (Split-Path $PSScriptRoot -Parent) 'third-party\prereqs'
New-Item -ItemType Directory -Path $dest -Force | Out-Null
$items = @(
    @{ Name = 'dotnet-desktop-runtime-x64.exe'; Url = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe'; Host = 'builds.dotnet.microsoft.com' },
    @{ Name = 'vc_redist.x64.exe'; Url = 'https://aka.ms/vs/17/release/vc_redist.x64.exe'; Host = 'download.visualstudio.microsoft.com' }
)
$report = foreach ($i in $items) {
    $file = Join-Path $dest $i.Name
    if (-not (Test-Path $file) -or $Force) {
        $temp = "$file.download"
        Invoke-WebRequest $i.Url -OutFile $temp -UseBasicParsing -MaximumRedirection 5
        $sig = Get-AuthenticodeSignature -LiteralPath $temp
        if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            Remove-Item $temp -Force
            throw "$($i.Name) is not validly signed by Microsoft ($($sig.Status), $($sig.SignerCertificate.Subject)). It was deleted."
        }
        Move-Item $temp $file -Force
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $file
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "$($i.Name) failed the signature check." }
    $v = (Get-Item $file).VersionInfo
    "{0}  {1}  {2:N1} MB  sha256 {3}" -f $i.Name, $v.ProductVersion, ((Get-Item $file).Length / 1MB), (Get-FileHash $file -Algorithm SHA256).Hash
}
Set-Content (Join-Path $dest 'prereqs.txt') $report -Encoding ascii
$report | ForEach-Object { Write-Host $_ }
