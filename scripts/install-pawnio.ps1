#Requires -Version 5.1
<#
.SYNOPSIS
Installs the official, signed PawnIO driver that LibreHardwareMonitorLib needs for motherboard fan and temperature sensors.

.DESCRIPTION
Runs elevated and executes a downloaded installer, so it refuses unless BOTH the SHA-256 matches -Sha256 AND
the Authenticode signature is Valid and made by the pinned -SignerThumbprint. There is deliberately no switch to bypass either check.
What protects the binary between verification and execution:
- it is downloaded into a fresh folder under Program Files, where non-admins cannot create or change anything,
  so the folder is admin-only from the moment it exists;
- the folder also gets a protected ACL (SYSTEM + Administrators only, no inheritance) and must be empty;
- the script refuses if the folder or the downloaded file is a reparse point (junction/symlink);
- the hash and signature are checked again immediately before the installer runs, by its full path.
This narrows but cannot eliminate a swap by an attacker who already has admin rights (they have already won).
The installer is never bundled with Ember (redistribution terms are unpublished; see docs/driver-research.md).

NOTE: the "-install -silent" switches come from third-party winget documentation and still need confirming on Windows.
#>
[CmdletBinding()]
param(
    # Release tag of github.com/namazso/PawnIO.Setup. Verified 2026-10-12.
    [ValidatePattern('^[0-9A-Za-z._-]+\z')]
    [string]$Version = '2.2.0',
    # SHA-256 of PawnIO_setup.exe for -Version (shasum -a 256; matches GitHub's published asset digest).
    [ValidatePattern('^[0-9A-Fa-f]{64}\z')]
    [string]$Sha256 = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032',
    # Authenticode signer certificate thumbprint (namazso.eu; read on Windows 2026-10-12; cert expires 2027-08-05).
    [ValidatePattern('^[0-9A-Fa-f]{40}\z')]
    [string]$SignerThumbprint = 'F380DCC9F706E2756A5047B832FFE719E1BC35F5'
)
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'install-pawnio.ps1 must be run from an elevated (Administrator) PowerShell.'
}

if ($Version -match '^\.+\z') { throw "Invalid -Version: $Version" }  # '.'/'..' would rewrite the URL path
$url = "https://github.com/namazso/PawnIO.Setup/releases/download/$Version/PawnIO_setup.exe"
if (-not $url.StartsWith('https://github.com/namazso/PawnIO.Setup/releases/download/', [StringComparison]::Ordinal)) {
    throw "Refusing unexpected download URL: $url"
}

# PowerShell 5.1 may default to TLS 1.0; GitHub needs TLS 1.2+.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Assert-NotReparsePoint([string]$path) {
    if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing: $path is a reparse point (junction or symlink)."
    }
}

# Hash AND signature, both required. Returns the signature for printing.
function Assert-Verified([string]$path) {
    Assert-NotReparsePoint $path
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if (-not [string]::Equals($actual, $Sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch: expected $Sha256, got $actual. Refusing to run the installer."
    }
    $sig = Get-AuthenticodeSignature -LiteralPath $path
    if ($sig.Status -ne [Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode signature is not valid ($($sig.Status): $($sig.StatusMessage)). Refusing to run the installer."
    }
    if (-not [string]::Equals($sig.SignerCertificate.Thumbprint, $SignerThumbprint, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected signer $($sig.SignerCertificate.Thumbprint) ($($sig.SignerCertificate.Subject)). Refusing to run the installer."
    }
    return $sig
}

# Fresh GUID folder under Program Files (known-folder API, not an env var). Users only have read & execute
# there, so no non-admin can plant files or turn the folder into a junction, even before Set-Acl runs.
# (ProgramData was rejected: Users may create folders there, which allows a junction race.)
$dir = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) ('Ember-pawnio-' + [Guid]::NewGuid().ToString('N'))
$created = $false
try {
    New-Item -ItemType Directory -Path $dir | Out-Null  # no -Force: fails if the name already exists
    $created = $true
    Assert-NotReparsePoint $dir
    # Defence in depth: SYSTEM + Administrators only, no inheritance.
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {  # SYSTEM, Administrators
        $id = New-Object Security.Principal.SecurityIdentifier $sid
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule $id, 'FullControl', $inherit, 'None', 'Allow'))
    }
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'))
    Set-Acl -LiteralPath $dir -AclObject $acl
    if (Get-ChildItem -LiteralPath $dir -Force) { throw "Refusing: $dir is not empty." }

    $exe = Join-Path $dir 'PawnIO_setup.exe'
    Write-Host "Downloading $url"
    $ProgressPreference = 'SilentlyContinue'  # the 5.1 progress bar makes Invoke-WebRequest very slow
    Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing -MaximumRedirection 5
    $exe = (Resolve-Path -LiteralPath $exe).ProviderPath

    $sig = Assert-Verified $exe
    Write-Host "SHA-256 OK: $Sha256"
    Write-Host "Signed by: $($sig.SignerCertificate.Subject)"
    Write-Host "Thumbprint: $($sig.SignerCertificate.Thumbprint)"

    # Re-verify right before running, by full path. Narrows, but cannot fully eliminate, a swap by an
    # admin-level attacker (who has already won).
    $null = Assert-Verified $exe
    $proc = Start-Process -FilePath $exe -ArgumentList '-install', '-silent' -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw "PawnIO installer failed with exit code $($proc.ExitCode)."
    }
    Write-Host 'PawnIO installed. Restart EmberService (Restart-Service EmberService) so it picks up the driver.'
}
finally {
    if ($created) {
        $item = Get-Item -LiteralPath $dir -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            [IO.Directory]::Delete($dir, $false)  # remove only the link; never recurse into its target
        } elseif ($item) {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
