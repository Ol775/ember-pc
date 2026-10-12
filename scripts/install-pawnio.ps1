#Requires -Version 5.1
<#
.SYNOPSIS
Installs the official, signed PawnIO driver that LibreHardwareMonitorLib needs for motherboard fan and temperature sensors.

.DESCRIPTION
Runs elevated and executes a downloaded installer, so it refuses unless BOTH the SHA-256 matches -Sha256 AND
the Authenticode signature is Valid. There is deliberately no switch to bypass either check.
The installer goes to a fresh folder under ProgramData that only SYSTEM and Administrators can write, so it
cannot be swapped between verification and execution. The installer is never bundled with Ember (redistribution
terms are unpublished; see docs/driver-research.md).

NOTE: the "-install -silent" switches come from third-party winget documentation and still need confirming on Windows.
#>
[CmdletBinding()]
param(
    # Release tag of github.com/namazso/PawnIO.Setup. Verified 2026-10-12.
    [ValidatePattern('^[0-9A-Za-z._-]+\z')]
    [string]$Version = '2.2.0',
    # SHA-256 of PawnIO_setup.exe for -Version (shasum -a 256; matches GitHub's published asset digest).
    [ValidatePattern('^[0-9A-Fa-f]{64}\z')]
    [string]$Sha256 = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032'
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

# Fresh GUID folder directly under ProgramData (known-folder API, not an env var). Not under a shared
# ProgramData\Ember parent: a non-admin could pre-create that and then delete/replace children.
$dir = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) ('ember-pawnio-' + [Guid]::NewGuid().ToString('N'))
$created = $false
try {
    # Create the folder, lock it to SYSTEM + Administrators (no inheritance), and only then download into it.
    New-Item -ItemType Directory -Path $dir | Out-Null  # no -Force: fails if the name already exists
    $created = $true
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) {  # SYSTEM, Administrators
        $id = New-Object Security.Principal.SecurityIdentifier $sid
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule $id, 'FullControl', $inherit, 'None', 'Allow'))
    }
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'))
    Set-Acl -LiteralPath $dir -AclObject $acl
    # Until Set-Acl, the folder inherited ProgramData's "Users may create files" grant. Anything planted in that
    # window would stay owned by its creator (who could rewrite it after verification), so require it empty.
    if (Get-ChildItem -LiteralPath $dir -Force) { throw "Refusing: $dir was modified before it was locked down." }

    $exe = Join-Path $dir 'PawnIO_setup.exe'
    Write-Host "Downloading $url"
    $ProgressPreference = 'SilentlyContinue'  # the 5.1 progress bar makes Invoke-WebRequest very slow
    Invoke-WebRequest -Uri $url -OutFile $exe -UseBasicParsing -MaximumRedirection 5

    $actual = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    if (-not [string]::Equals($actual, $Sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch: expected $Sha256, got $actual. Refusing to run the installer."
    }
    Write-Host "SHA-256 OK: $actual"

    $sig = Get-AuthenticodeSignature -LiteralPath $exe
    if ($sig.Status -ne [Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode signature is not valid ($($sig.Status): $($sig.StatusMessage)). Refusing to run the installer."
    }
    Write-Host "Signed by: $($sig.SignerCertificate.Subject)"
    Write-Host "Thumbprint: $($sig.SignerCertificate.Thumbprint)"

    $proc = Start-Process -FilePath $exe -ArgumentList '-install', '-silent' -Wait -PassThru
    if ($proc.ExitCode -ne 0) {
        throw "PawnIO installer failed with exit code $($proc.ExitCode)."
    }
    Write-Host 'PawnIO installed. Restart EmberService (Restart-Service EmberService) so it picks up the driver.'
}
finally {
    if ($created) { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue }
}
