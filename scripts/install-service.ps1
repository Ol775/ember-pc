#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExePath,
    # Dev builds only: skip the check that the exe and its folder are not writable by non-admins.
    [switch]$AllowInsecurePath
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
$writeRights = [Security.AccessControl.FileSystemRights]::Write -bor
    [Security.AccessControl.FileSystemRights]::WriteData -bor
    [Security.AccessControl.FileSystemRights]::CreateFiles -bor
    [Security.AccessControl.FileSystemRights]::AppendData -bor
    [Security.AccessControl.FileSystemRights]::Modify -bor
    [Security.AccessControl.FileSystemRights]::FullControl -bor
    [Security.AccessControl.FileSystemRights]::Delete -bor
    [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
    [Security.AccessControl.FileSystemRights]::TakeOwnership
$unsafe = foreach ($target in @($exe, (Split-Path -Parent $exe))) {
    foreach ($rule in (Get-Acl -LiteralPath $target).Access) {
        if ($rule.AccessControlType -ne 'Allow') { continue }
        $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if (($badSids -contains $sid) -and ($rule.FileSystemRights -band $writeRights)) {
            "$target is writable by $($rule.IdentityReference) ($($rule.FileSystemRights))"
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

& sc.exe query EmberService *> $null
if ($LASTEXITCODE -eq 0) {
    throw 'EmberService already exists. Run uninstall-service.ps1 first.'
}

# New-Service avoids sc.exe's native-argument quoting quirks, which differ between PowerShell versions.
New-Service -Name EmberService -BinaryPathName "`"$exe`"" -DisplayName 'Ember' -StartupType Automatic | Out-Null

function Remove-Failed { & sc.exe delete EmberService *> $null }

# The stored path must be quoted, otherwise a path with spaces is an unquoted-service-path hole.
$imagePath = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\EmberService').ImagePath
if (-not ($imagePath.StartsWith('"') -and $imagePath.EndsWith('"'))) {
    Remove-Failed
    throw "ImagePath was stored unquoted ($imagePath); the service was removed."
}

& sc.exe failure EmberService reset= 86400 actions= restart/5000/restart/5000/restart/30000
if ($LASTEXITCODE -ne 0) { Remove-Failed; throw "sc.exe failure failed (exit $LASTEXITCODE); the service was removed." }

# Also trigger recovery when the service stops with a non-zero exit code, not only on a crash.
& sc.exe failureflag EmberService 1
if ($LASTEXITCODE -ne 0) { Remove-Failed; throw "sc.exe failureflag failed (exit $LASTEXITCODE); the service was removed." }

Start-Service EmberService
