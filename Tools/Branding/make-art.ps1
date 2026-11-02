# Generuje grafiku DMX Visualiser (store art, ikona aplikace, stereo splash) pomocí System.Drawing.
# Spuštění:  pwsh -File Tools\Branding\make-art.ps1
# Výstup:    Store\*.png (Meta Developer Dashboard), Assets\Branding\DMXIcon.png, Assets\Branding\DMXSplash.png
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..\..").Path
New-Item -ItemType Directory -Force "$root\Store", "$root\Assets\Branding" | Out-Null

function C([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
$cWhite = C 255 242 246 255
$cBlue  = C 255 255 92 200
$cSub   = C 255 176 170 205
$cLine  = C 235 235 242 255
$beamCols = @((C 255 255 60 170), (C 255 60 220 255), (C 255 255 190 50), (C 255 140 90 255), (C 255 70 255 150))

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    return @{ bmp = $bmp; g = $g; w = $w; h = $h }
}

# téměř černé pozadí s fialovou září u zdroje světel
function Draw-Background($cv, [float]$gx, [float]$gy) {
    $g = $cv.g
    $g.FillRectangle((New-Object System.Drawing.SolidBrush (C 255 8 6 16)), 0, 0, $cv.w, $cv.h)
    $r = [Math]::Max($cv.w, $cv.h) * 0.8
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse([float]($gx - $r), [float]($gy - $r), [float](2 * $r), [float](2 * $r))
    $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $pg.CenterColor = C 120 90 40 150
    $pg.SurroundColors = @((C 0 8 6 16))
    $g.FillPath($pg, $path)
}

# izometrická projekce (jednotky scény -> pixely)
function Iso([float]$x, [float]$y, [float]$z, [float]$cx, [float]$cy, [float]$s) {
    New-Object System.Drawing.PointF ([float]($cx + ($x - $y) * 0.866 * $s)), ([float]($cy + ($x + $y) * 0.5 * $s - $z * $s))
}

function Pen-Of($col, [float]$w) {
    $p = New-Object System.Drawing.Pen $col, $w
    $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p.LineJoin = 'Round'
    return $p
}

# Emblém: vějíř barevných paprsků ze zdroje (ox,oy) směrem nahoru, délka len, rozevření spread (stupně na každou stranu)
function Draw-Emblem($cv, [float]$ox, [float]$oy, [float]$len, [float]$spread = 62) {
    $g = $cv.g
    $n = 9
    # haze u zdroje
    $hr = $len * 0.42
    $hp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $hp.AddEllipse([float]($ox - $hr), [float]($oy - $hr * 0.55), [float](2 * $hr), [float](1.1 * $hr))
    $hb = New-Object System.Drawing.Drawing2D.PathGradientBrush $hp
    $hb.CenterColor = C 150 255 140 230; $hb.SurroundColors = @((C 0 255 140 230))
    $g.FillPath($hb, $hp)
    for ($pass = 0; $pass -lt 2; $pass++) {
        for ($i = 0; $i -lt $n; $i++) {
            $t = $i / ($n - 1)
            $ang = (-$spread + 2 * $spread * $t) * [Math]::PI / 180
            $col = $beamCols[$i % $beamCols.Count]
            $half = if ($pass -eq 0) { 5.0 } else { 1.6 }       # úhlová polovina šířky paprsku (měkký + jádro)
            $alpha = if ($pass -eq 0) { 120 } else { 210 }
            $L = $len * (0.82 + 0.18 * [Math]::Cos($ang * 1.3))
            $a1 = $ang - $half * [Math]::PI / 180; $a2 = $ang + $half * [Math]::PI / 180
            $p1 = New-Object System.Drawing.PointF ([float]($ox + [Math]::Sin($a1) * $L)), ([float]($oy - [Math]::Cos($a1) * $L))
            $p2 = New-Object System.Drawing.PointF ([float]($ox + [Math]::Sin($a2) * $L)), ([float]($oy - [Math]::Cos($a2) * $L))
            $o = New-Object System.Drawing.PointF $ox, $oy
            $tip = New-Object System.Drawing.PointF ([float]($ox + [Math]::Sin($ang) * $L)), ([float]($oy - [Math]::Cos($ang) * $L))
            $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $o, $tip, (C $alpha $col.R $col.G $col.B), (C 0 $col.R $col.G $col.B)
            $g.FillPolygon($br, @($o, $p1, $p2))
        }
    }
    # zdroj
    $sr = $len * 0.045
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C 90 255 255 255)), [float]($ox - $sr * 2.6), [float]($oy - $sr * 2.6), [float]($sr * 5.2), [float]($sr * 5.2))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C 255 255 255 255)), [float]($ox - $sr), [float]($oy - $sr), [float]($sr * 2), [float]($sr * 2))
}

function Draw-Wordmark($cv, [float]$x, [float]$y, [float]$size, [string]$align) {
    $g = $cv.g
    $f = New-Object System.Drawing.Font 'Segoe UI Black', $size, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat; $sf.FormatFlags = 'NoWrap'
    $m1 = $g.MeasureString('DMX ', $f, 9999, $sf).Width - $size * 0.12
    $m2 = $g.MeasureString('Visualiser', $f, 9999, $sf).Width - $size * 0.12
    $tot = $m1 + $m2
    $x0 = switch ($align) { 'center' { $x - $tot / 2 } default { $x } }
    $g.DrawString('DMX', $f, (New-Object System.Drawing.SolidBrush $cWhite), $x0, $y, $sf)
    $g.DrawString('Visualiser', $f, (New-Object System.Drawing.SolidBrush $cBlue), $x0 + $m1, $y, $sf)
    return $tot
}

function Draw-Sub($cv, [string]$text, [float]$x, [float]$y, [float]$size, [string]$align) {
    $g = $cv.g
    $f = New-Object System.Drawing.Font 'Segoe UI Semilight', $size, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    if ($align -eq 'center') { $sf.Alignment = 'Center' }
    $g.DrawString($text, $f, (New-Object System.Drawing.SolidBrush $cSub), $x, $y, $sf)
}

function Save-Canvas($cv, [string]$path) {
    $cv.bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $cv.g.Dispose(); $cv.bmp.Dispose()
    Write-Host ("{0}  {1}x{2}" -f $path.Replace($root + '\', ''), $cv.w, $cv.h)
}

$tag1 = 'Art-Net and sACN lights, live in VR.'
$tag2 = 'See the show before the gig.'

# 1) ikona 512 + zdroj pro ikonu aplikace (1024, emblém uvnitř bezpečné zóny adaptivní ikony)
foreach ($sz in 512, 1024) {
    $cv = New-Canvas $sz $sz
    Draw-Background $cv ($sz / 2) ($sz * 0.78)
    Draw-Emblem $cv ($sz * 0.5) ($sz * 0.80) ($sz * 0.60) 58
    $out = if ($sz -eq 512) { "$root\Store\icon_512.png" } else { "$root\Assets\Branding\DMXIcon.png" }
    Save-Canvas $cv $out
}

# 2) čtvercová obálka 1440
$cv = New-Canvas 1440 1440
Draw-Background $cv 720 800
Draw-Emblem $cv 720 840 760 62
Draw-Wordmark $cv 720 880 150 'center' | Out-Null
Draw-Sub $cv $tag1 720 1180 54 'center'
Save-Canvas $cv "$root\Store\cover_square_1440.png"

# 3) obálka na šířku 2560x1440
$cv = New-Canvas 2560 1440
Draw-Background $cv 2000 1000
Draw-Emblem $cv 2010 1230 1000 38
$w = Draw-Wordmark $cv 170 420 160 'left'
Draw-Sub $cv ($tag1 + "`n" + $tag2) 176 650 60 'left'
Save-Canvas $cv "$root\Store\cover_landscape_2560x1440.png"

# 4) obálka na výšku 1008x1440
$cv = New-Canvas 1008 1440
Draw-Background $cv 504 800
Draw-Emblem $cv 504 880 700 62
Draw-Wordmark $cv 504 930 112 'center' | Out-Null
Draw-Sub $cv ($tag1 + "`n" + $tag2) 504 1120 44 'center'
Save-Canvas $cv "$root\Store\cover_portrait_1008x1440.png"

# 5) hero 3000x900
$cv = New-Canvas 3000 900
Draw-Background $cv 2250 900
Draw-Emblem $cv 2250 1000 880 52
Draw-Wordmark $cv 230 250 190 'left' | Out-Null
Draw-Sub $cv ($tag1 + "`n" + $tag2) 236 520 62 'left'
Save-Canvas $cv "$root\Store\hero_3000x900.png"

# 6) mini 1080x360
$cv = New-Canvas 1080 360
Draw-Background $cv 860 330
Draw-Emblem $cv 860 345 300 56
Draw-Wordmark $cv 50 95 78 'left' | Out-Null
Draw-Sub $cv $tag2 54 200 30 'left'
Save-Canvas $cv "$root\Store\mini_landscape_1080x360.png"

# 7) stereo splash 3200x1000 (2x 1600x1000, shodné pro obě oči)
$cv = New-Canvas 3200 1000
foreach ($eye in 0, 1) {
    $ox = $eye * 1600
    $cv.g.SetClip((New-Object System.Drawing.Rectangle $ox, 0, 1600, 1000))
    $sub = New-Canvas 1600 1000
    Draw-Background $sub 800 560
    Draw-Emblem $sub 800 600 520 60
    Draw-Wordmark $sub 800 640 120 'center' | Out-Null
    $cv.g.DrawImage($sub.bmp, $ox, 0, 1600, 1000)
    $sub.g.Dispose(); $sub.bmp.Dispose()
    $cv.g.ResetClip()
}
Save-Canvas $cv "$root\Assets\Branding\DMXSplash.png"
