#Requires -Version 5.1
# Packs the Stream Deck plugin into a .streamDeckPlugin file (a zip with dev.ember.pc.sdPlugin at its root): the plugin program from
# -PublishDir, the manifest (its Version set from -Version, e.g. 1.2.0-beta.1 becomes 1.2.0.1), the settings page and the icons.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Out
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$plugin = Join-Path $root 'src\Ember.StreamDeck\plugin'
if (-not ($Version -match '^(\d+)\.(\d+)\.(\d+)(?:-[A-Za-z]+\.(\d+))?')) { throw "Cannot read the version '$Version'." }
$four = '{0}.{1}.{2}.{3}' -f $Matches[1], $Matches[2], $Matches[3], $(if ($Matches[4]) { $Matches[4] } else { '0' })

$work = Join-Path ([IO.Path]::GetTempPath()) ('ember-sd-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $work 'dev.ember.pc.sdPlugin'
try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item (Join-Path $PublishDir '*') $stage -Recurse -Force
    Get-ChildItem $stage -Filter '*.pdb' | Remove-Item -Force
    Copy-Item (Join-Path $plugin 'pi') $stage -Recurse
    Copy-Item (Join-Path $plugin 'images') $stage -Recurse
    $manifest = (Get-Content (Join-Path $plugin 'manifest.json') -Raw) -replace '"Version":\s*"[^"]*"', ('"Version": "' + $four + '"')
    [IO.File]::WriteAllText((Join-Path $stage 'manifest.json'), $manifest, (New-Object Text.UTF8Encoding $false))

    New-Item -ItemType Directory -Path (Split-Path $Out -Parent) -Force | Out-Null
    if (Test-Path $Out) { Remove-Item $Out -Force }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    # Entries are written one by one with forward slashes (Windows PowerShell and .NET Framework would otherwise store backslashes).
    $zip = [IO.Compression.ZipFile]::Open($Out, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $base = $work.TrimEnd('\') + '\'
        foreach ($f in Get-ChildItem $stage -Recurse -File) {
            $name = $f.FullName.Substring($base.Length).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $zip.Dispose() }
    Write-Host "Packed $Out (plugin version $four)"
}
finally { if (Test-Path $work) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue } }
