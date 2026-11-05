# Generates the Joe Browser application icon (multi-size PNG-in-ICO)
Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-LogoPng([int]$S) {
    $bmp = New-Object Drawing.Bitmap $S, $S
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([Drawing.Color]::Transparent)

    $pad = [float]($S * 0.035)
    $path = New-RoundedPath $pad $pad ($S - 2 * $pad) ($S - 2 * $pad) ($S * 0.25)
    $c1 = [Drawing.Color]::FromArgb(255, 124, 92, 255)
    $c2 = [Drawing.Color]::FromArgb(255, 56, 132, 255)
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush(
        (New-Object Drawing.PointF 0, 0),
        (New-Object Drawing.PointF $S, $S), $c1, $c2)
    $g.FillPath($brush, $path)

    # soft top highlight
    $hi = New-RoundedPath ($pad * 2) ($pad * 1.6) ($S - 4 * $pad) ($S * 0.42) ($S * 0.18)
    $hiBrush = New-Object Drawing.Drawing2D.LinearGradientBrush(
        (New-Object Drawing.PointF(0, 0)),
        (New-Object Drawing.PointF(0, ($S * 0.45))),
        [Drawing.Color]::FromArgb(46, 255, 255, 255),
        [Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($hiBrush, $hi)

    $font = New-Object Drawing.Font("Segoe UI", ($S * 0.54), [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object Drawing.StringFormat
    $sf.Alignment = 'Center'
    $sf.LineAlignment = 'Center'
    $rect = New-Object Drawing.RectangleF(0, ($S * 0.015), $S, $S)
    $g.DrawString("J", $font, [Drawing.Brushes]::White, $rect, $sf)

    # accent dot
    $dot = [float]($S * 0.085)
    $db = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(235, 255, 255, 255))
    $g.FillEllipse($db, ($S * 0.70), ($S * 0.70), $dot, $dot)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $g.Dispose(); $bmp.Dispose(); $ms.Dispose()
    return ,$bytes
}

$sizes = @(256, 64, 48, 32, 16)
$images = @()
foreach ($s in $sizes) { $images += ,(New-LogoPng $s) }

$out = Join-Path $PSScriptRoot "..\src\app.ico"
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $b = $images[$i]
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$b.Length); $bw.Write([UInt32]$offset)
    $offset += $b.Length
}
foreach ($b in $images) { $bw.Write($b) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Output ("icon written: {0} ({1} bytes)" -f $out, (Get-Item $out).Length)

