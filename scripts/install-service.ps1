#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExePath,
    # Dev builds only: skip the check that the exe and its folder are not writable by non-admins.
    [switch]$AllowInsecurePath,
    [string]$ServiceName = 'EmberService',
    [string]$DisplayName = 'Ember'
)
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'install-service.ps1 must be run from an elevated (Administrator) PowerShell.'
}
if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    throw "ExePath does not exist: $ExePath"
}
$exe = (Resolve-Path -LiteralPath $ExePath).ProviderPath

# The service runs as LocalSystem, so anyone who can write the exe or its folder could escalate to SYSTEM.
$badSids = @('S-1-5-32-545', 'S-1-5-11', 'S-1-1-0', 'S-1-5-4')  # Users, Authenticated Users, Everyone, Interactive
# Write-type bits only. Composite masks (Modify, FullControl, Write) contain READ bits, which would match every read grant.
# 0x500D0156 = WriteData/CreateFiles | AppendData | WriteEA | WriteAttributes | DeleteSubdirectoriesAndFiles | Delete |
#              ChangePermissions | TakeOwnership | GENERIC_ALL | GENERIC_WRITE
$writeRights = 0x500D0156
function Test-WriteRight([int]$rights) { return (($rights -band $writeRights) -ne 0) }
$unsafe = foreach ($target in @($exe, (Split-Path -Parent $exe))) {
    $acl = Get-Acl -LiteralPath $target
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($badSids -contains $owner) { "$target is owned by $owner, who can change its permissions" }
    # Ask for SIDs directly: Translate() throws for unmappable ACEs such as ALL APPLICATION PACKAGES.
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        if (($badSids -contains $rule.IdentityReference.Value) -and (Test-WriteRight ([int]$rule.FileSystemRights))) {
            "$target is writable by $($rule.IdentityReference.Value) ($($rule.FileSystemRights))"
        }
    }
}
if ($unsafe) {
    if ($AllowInsecurePath) {
        Write-Warning 'INSECURE PATH ALLOWED: a non-admin user can replace the service binary and run code as SYSTEM.'
        $unsafe | ForEach-Object { Write-Warning $_ }
    } else {
        throw ("Refusing to install: non-admins can modify the service binary or its folder (privilege escalation).`n" +
            ($unsafe -join "`n") + "`nMove it somewhere only admins can write (e.g. C:\Program Files\Ember), or pass -AllowInsecurePath for a dev build.")
    }
}

& sc.exe query $ServiceName *> $null
if ($LASTEXITCODE -eq 0) {
    throw "$ServiceName already exists. Run the matching uninstall script first."
}

# New-Service avoids sc.exe's native-argument quoting quirks, which differ between PowerShell versions.
New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName $DisplayName -StartupType Automatic | Out-Null

function Remove-Failed { & sc.exe delete $ServiceName *> $null }

# The stored path must be quoted, otherwise a path with spaces is an unquoted-service-path hole.
$imagePath = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName").ImagePath
if (-not ($imagePath.StartsWith('"') -and $imagePath.EndsWith('"'))) {
    Remove-Failed
    throw "ImagePath was stored unquoted ($imagePath); the service was removed."
}

& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000
if ($LASTEXITCODE -ne 0) { Remove-Failed; throw "sc.exe failure failed (exit $LASTEXITCODE); the service was removed." }

# Also trigger recovery when the service stops with a non-zero exit code, not only on a crash.
& sc.exe failureflag $ServiceName 1
if ($LASTEXITCODE -ne 0) { Remove-Failed; throw "sc.exe failureflag failed (exit $LASTEXITCODE); the service was removed." }

try { Start-Service $ServiceName }
catch {
    Write-Warning 'The service was created but failed to start; check the Windows Event Log.'
    throw
}
