<#
  Builds Ember's logo files from one set of shapes:
    docs/images/logo.svg, logo-mark.svg, logo.png (1024) and src/Ember.App/Assets/ember.ico (16..256).
  Run:  powershell -File scripts\make-icon.ps1
#>
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$images = Join-Path $root 'docs\images'
$assets = Join-Path $root 'src\Ember.App\Assets'
New-Item -ItemType Directory -Force $images, $assets | Out-Null

# Shapes live in a 256 x 256 box. Only absolute M, C and Z are used so the SVG and the bitmap renderer agree.
$outer = 'M128 30 C152 66 198 90 198 148 C198 190 167 222 128 222 C89 222 58 190 58 148 C58 120 73 100 88 85 C92 106 102 119 115 123 C104 86 116 58 128 30 Z'
$inner = 'M128 116 C141 138 166 149 166 178 C166 199 149 212 128 212 C107 212 90 199 90 178 C90 163 99 153 108 144 C112 155 118 160 125 160 C120 143 122 130 128 116 Z'
$tileRadius = 56

function New-Shape([string]$d, [double]$scale, [double]$ox, [double]$oy) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pen = $null   # current point
    foreach ($m in [regex]::Matches($d, '([MCZ])([^MCZ]*)')) {
        $n = [regex]::Matches($m.Groups[2].Value, '-?\d+(\.\d+)?') | ForEach-Object { [double]$_.Value }
        $pt = { param($k) New-Object System.Drawing.PointF([single]($n[$k] * $scale + $ox), [single]($n[$k + 1] * $scale + $oy)) }
        switch ($m.Groups[1].Value) {
            'M' { $path.StartFigure(); $pen = & $pt 0 }
            'C' { $p1 = & $pt 0; $p2 = & $pt 2; $p3 = & $pt 4; $path.AddBezier($pen, $p1, $p2, $p3); $pen = $p3 }
            'Z' { $path.CloseFigure() }
        }
    }
    $path
}

function New-Tile([double]$size, [double]$radius) {
    $d = $radius * 2
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc(0, 0, $d, $d, 180, 90); $p.AddArc($size - $d, 0, $d, $d, 270, 90)
    $p.AddArc($size - $d, $size - $d, $d, $d, 0, 90); $p.AddArc(0, $size - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}

function Hex($h, $a = 255) { $c = [System.Drawing.ColorTranslator]::FromHtml($h); [System.Drawing.Color]::FromArgb($a, $c) }

function Draw([int]$size, [bool]$withTile) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    if ($withTile) {
        $tile = New-Tile $size ($tileRadius * $s)
        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($size, $size)), (Hex '#2e1a12'), (Hex '#120d0b'))
        $g.FillPath($bg, $tile)
        # soft glow behind the flame
        $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $glowPath.AddEllipse([single](28 * $s), [single](40 * $s), [single](200 * $s), [single](200 * $s))
        $glow = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath)
        $glow.CenterColor = Hex '#ff5a1f' 110; $glow.SurroundColors = @((Hex '#ff5a1f' 0))
        $g.SetClip($tile); $g.FillPath($glow, $glowPath); $g.ResetClip()
    }
    $flame = New-Shape $outer $s 0 0
    $fb = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, [single](222 * $s))), (New-Object System.Drawing.PointF(0, [single](30 * $s))), (Hex '#ff4d12'), (Hex '#ffb347'))
    $g.FillPath($fb, $flame)
    $core = New-Shape $inner $s 0 0
    $cb = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, [single](212 * $s))), (New-Object System.Drawing.PointF(0, [single](116 * $s))), (Hex '#ffc15a'), (Hex '#fff1c9'))
    $g.FillPath($cb, $core)
    $g.Dispose(); $bmp
}

function Png-Bytes($bmp) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $ms.ToArray() }

# --- SVG -------------------------------------------------------------------------------------------------------------
$defs = @"
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#2e1a12"/><stop offset="1" stop-color="#120d0b"/></linearGradient>
    <radialGradient id="glow" cx="0.5" cy="0.55" r="0.5"><stop offset="0" stop-color="#ff5a1f" stop-opacity="0.43"/><stop offset="1" stop-color="#ff5a1f" stop-opacity="0"/></radialGradient>
    <linearGradient id="flame" x1="0" y1="1" x2="0" y2="0"><stop offset="0" stop-color="#ff4d12"/><stop offset="1" stop-color="#ffb347"/></linearGradient>
    <linearGradient id="core" x1="0" y1="1" x2="0" y2="0"><stop offset="0" stop-color="#ffc15a"/><stop offset="1" stop-color="#fff1c9"/></linearGradient>
  </defs>
"@
$svgTile = @"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
$defs
  <rect width="256" height="256" rx="$tileRadius" fill="url(#tile)"/>
  <rect width="256" height="256" rx="$tileRadius" fill="url(#glow)"/>
  <path d="$outer" fill="url(#flame)"/>
  <path d="$inner" fill="url(#core)"/>
</svg>
"@
$svgMark = @"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
$defs
  <path d="$outer" fill="url(#flame)"/>
  <path d="$inner" fill="url(#core)"/>
</svg>
"@
[System.IO.File]::WriteAllText((Join-Path $images 'logo.svg'), $svgTile, (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText((Join-Path $images 'logo-mark.svg'), $svgMark, (New-Object System.Text.UTF8Encoding($false)))

# --- PNG and ICO -----------------------------------------------------------------------------------------------------
$big = Draw 1024 $true; $big.Save((Join-Path $images 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = New-Object System.Collections.Generic.List[byte[]]
foreach ($z in $sizes) { $b = Draw $z $true; $pngs.Add([byte[]](Png-Bytes $b)); $b.Dispose() }
$ms = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter($ms)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $z = $sizes[$i]; $dim = if ($z -ge 256) { 0 } else { $z }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'ember.ico'), $ms.ToArray())
Write-Host "Wrote logo.svg, logo-mark.svg, logo.png and ember.ico ($($ms.Length) bytes, sizes $($sizes -join ', '))"
