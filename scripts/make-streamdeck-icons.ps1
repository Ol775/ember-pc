#Requires -Version 5.1
# Draws the Stream Deck plugin's icons (src\Ember.StreamDeck\plugin\images) in Ember's colours (accent orange #ff7a2f, dark #1c1c1e):
#   plugin, category, key: Ember's flame in orange on a dark rounded tile (same shape as scripts\make-icon.ps1)
#   one per action: an orange rounded tile with a dark picture (bulb, sun, fan, ...), which reads on Stream Deck's dark action list.
#     The pictures are the paths in src\Ember.Core\StreamDeckGlyphs.json, the same ones the key faces draw.
# Each is written as <name>.png and <name>@2x.png. Run it again after changing the list below; the PNG files are committed.
param([string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Ember.StreamDeck\plugin\images'))
Add-Type -AssemblyName System.Drawing, PresentationCore, PresentationFramework, WindowsBase
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$root = Split-Path $PSScriptRoot -Parent

$orange = [System.Drawing.ColorTranslator]::FromHtml('#ff7a2f')
$dark = [System.Drawing.ColorTranslator]::FromHtml('#1c1c1e')
# Shapes live in a 256 x 256 box; only absolute M, C and Z are used.
$flameOuter = 'M128 30 C152 66 198 90 198 148 C198 190 167 222 128 222 C89 222 58 190 58 148 C58 120 73 100 88 85 C92 106 102 119 115 123 C104 86 116 58 128 30 Z'
$flameInner = 'M128 116 C141 138 166 149 166 178 C166 199 149 212 128 212 C107 212 90 199 90 178 C90 163 99 153 108 144 C112 155 118 160 125 160 C120 143 122 130 128 116 Z'

function New-Shape([string]$d, [double]$scale) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pen = $null
    foreach ($m in [regex]::Matches($d, '([MCZ])([^MCZ]*)')) {
        $n = [regex]::Matches($m.Groups[2].Value, '-?\d+(\.\d+)?') | ForEach-Object { [double]$_.Value }
        switch ($m.Groups[1].Value) {
            'M' { $path.StartFigure(); $pen = New-Object System.Drawing.PointF([single]($n[0] * $scale), [single]($n[1] * $scale)) }
            'C' {
                $p1 = New-Object System.Drawing.PointF([single]($n[0] * $scale), [single]($n[1] * $scale))
                $p2 = New-Object System.Drawing.PointF([single]($n[2] * $scale), [single]($n[3] * $scale))
                $p3 = New-Object System.Drawing.PointF([single]($n[4] * $scale), [single]($n[5] * $scale))
                $path.AddBezier($pen, $p1, $p2, $p3); $pen = $p3
            }
            'Z' { $path.CloseFigure() }
        }
    }
    $path
}

function New-Tile([int]$px, [double]$radius) {
    $d = $radius * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc(0, 0, $d, $d, 180, 90); $p.AddArc($px - $d - 1, 0, $d, $d, 270, 90)
    $p.AddArc($px - $d - 1, $px - $d - 1, $d, $d, 0, 90); $p.AddArc(0, $px - $d - 1, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

function Save-Flame([string]$file, [int]$px) {
    $bmp = New-Object System.Drawing.Bitmap $px, $px
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.FillPath((New-Object System.Drawing.SolidBrush $dark), (New-Tile $px ($px * 0.22)))
    $k = $px / 256.0
    $g.FillPath((New-Object System.Drawing.SolidBrush $orange), (New-Shape $flameOuter $k))
    $g.FillPath((New-Object System.Drawing.SolidBrush $dark), (New-Shape $flameInner $k))
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$glyphs = Get-Content (Join-Path $root 'src\Ember.Core\StreamDeckGlyphs.json') -Raw | ConvertFrom-Json
function Save-Glyph([string]$file, [int]$px, [string]$name) {
    $brush = { param($hex) New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color][System.Windows.Media.ColorConverter]::ConvertFromString($hex)) }
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $radius = $px * 0.22
    $dc.DrawRoundedRectangle((& $brush '#ff7a2f'), $null, (New-Object System.Windows.Rect 0, 0, $px, $px), $radius, $radius)
    $pen = New-Object System.Windows.Media.Pen (& $brush '#1c1c1e'), 2.2
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'; $pen.LineJoin = 'Round'
    $s = $px * 0.72 / 24.0
    $offset = $px * 0.14
    $dc.PushTransform((New-Object System.Windows.Media.MatrixTransform $s, 0, 0, $s, $offset, $offset))
    foreach ($d in $glyphs.$name) { $dc.DrawGeometry($null, $pen, [System.Windows.Media.Geometry]::Parse($d)) }
    $dc.Pop()
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $px, $px, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $fs = [System.IO.File]::Create($file)
    try { $enc.Save($fs) } finally { $fs.Dispose() }
}

# name, base size in pixels, 'flame' or the glyph's name
$icons = @(
    @('plugin', 256, 'flame'), @('category', 28, 'flame'), @('key', 72, 'flame'),
    @('scene', 20, 'scene'), @('power', 20, 'bulb'), @('brightness', 20, 'sun'), @('fanmode', 20, 'fan'),
    @('cycle', 20, 'cycle'), @('open', 20, 'open'), @('readout', 20, 'thermometer')
)
foreach ($i in $icons) {
    $name = $i[0]; $size = [int]$i[1]; $kind = $i[2]
    foreach ($scale in 1, 2) {
        $px = $size * $scale
        $file = Join-Path $OutDir ($(if ($scale -eq 1) { "$name.png" } else { "$name@2x.png" }))
        if ($kind -eq 'flame') { Save-Flame $file $px } else { Save-Glyph $file $px $kind }
    }
}
Write-Host "Wrote $($icons.Count * 2) icons to $OutDir"
