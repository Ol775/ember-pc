#Requires -Version 5.1
# Draws the Stream Deck plugin's icons (src\Ember.StreamDeck\plugin\images): an amber rounded square with a white letter, as
# <name>.png and <name>@2x.png. Run it again after changing the list below; the PNG files are committed.
param([string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Ember.StreamDeck\plugin\images'))
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# name, base size in pixels, letter
$icons = @(
    @('plugin', 256, 'E'), @('category', 28, 'E'), @('key', 72, 'E'),
    @('scene', 20, 'S'), @('power', 20, 'L'), @('brightness', 20, 'B'), @('fanmode', 20, 'F'),
    @('cycle', 20, 'C'), @('open', 20, 'O'), @('readout', 20, 'T')
)
foreach ($i in $icons) {
    $name = $i[0]; $size = [int]$i[1]; $letter = $i[2]
    foreach ($scale in 1, 2) {
        $px = $size * $scale
        $bmp = New-Object System.Drawing.Bitmap $px, $px
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'AntiAliasGridFit'
        $g.Clear([System.Drawing.Color]::Transparent)
        $r = [int]($px * 0.22)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($px - $d - 1, 0, $d, $d, 270, 90)
        $path.AddArc($px - $d - 1, $px - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $px - $d - 1, $d, $d, 90, 90)
        $path.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 140, 20))), $path)
        $font = New-Object System.Drawing.Font 'Segoe UI', ([single]($px * 0.5)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
        $g.DrawString($letter, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, $px, $px), $fmt)
        $file = Join-Path $OutDir ($(if ($scale -eq 1) { "$name.png" } else { "$name@2x.png" }))
        $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
    }
}
Write-Host "Wrote $($icons.Count * 2) icons to $OutDir"
