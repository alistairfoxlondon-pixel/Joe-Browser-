# Generates the Joe Browser application icon (multi-size PNG-in-ICO).
# The artwork intentionally matches Logo.cs (shield + white J + windows-ui accent
# gradient) so the taskbar, title bar, header and About page show the same mark.
Add-Type -AssemblyName System.Drawing

function New-ShieldPath([float]$x, [float]$y, [float]$w, [float]$h) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $p.StartFigure()
    $p.AddBezier(
        ($x + $w * 0.50), ($y + $h * 0.02),
        ($x + $w * 0.66), ($y + $h * 0.12),
        ($x + $w * 0.84), ($y + $h * 0.15),
        ($x + $w * 0.97), ($y + $h * 0.15))
    $p.AddLine(($x + $w * 0.97), ($y + $h * 0.15), ($x + $w * 0.97), ($y + $h * 0.54))
    $p.AddBezier(
        ($x + $w * 0.97), ($y + $h * 0.54),
        ($x + $w * 0.97), ($y + $h * 0.76),
        ($x + $w * 0.78), ($y + $h * 0.91),
        ($x + $w * 0.50), ($y + $h * 0.98))
    $p.AddBezier(
        ($x + $w * 0.50), ($y + $h * 0.98),
        ($x + $w * 0.22), ($y + $h * 0.91),
        ($x + $w * 0.03), ($y + $h * 0.76),
        ($x + $w * 0.03), ($y + $h * 0.54))
    $p.AddLine(($x + $w * 0.03), ($y + $h * 0.54), ($x + $w * 0.03), ($y + $h * 0.15))
    $p.AddBezier(
        ($x + $w * 0.03), ($y + $h * 0.15),
        ($x + $w * 0.16), ($y + $h * 0.15),
        ($x + $w * 0.34), ($y + $h * 0.12),
        ($x + $w * 0.50), ($y + $h * 0.02))
    $p.CloseFigure()
    return $p
}

function New-LogoPng([int]$S) {
    $bmp = New-Object Drawing.Bitmap $S, $S
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    $pad = [float]($S * 0.055)
    $size = $S - 2 * $pad
    $box = New-Object Drawing.RectangleF $pad, $pad, $size, $size
    $path = New-ShieldPath $pad $pad $size $size

    # windows-ui accent gradient: #0078D7 -> #60CDFF
    $c1 = [Drawing.Color]::FromArgb(255, 0, 120, 215)
    $c2 = [Drawing.Color]::FromArgb(255, 96, 205, 255)
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush(
        (New-Object Drawing.PointF $pad, $pad),
        (New-Object Drawing.PointF ($pad + $size), ($pad + $size)), $c1, $c2)
    $g.FillPath($brush, $path)

    # soft top sheen, clipped to the shield so it never leaks outside the shape
    if ($S -ge 48) {
        $oldClip = $g.Clip
        $g.SetClip($path)
        $sheen = New-Object Drawing.Drawing2D.LinearGradientBrush(
            (New-Object Drawing.PointF 0, $pad),
            (New-Object Drawing.PointF 0, ($pad + $size * 0.55)),
            [Drawing.Color]::FromArgb(52, 255, 255, 255),
            [Drawing.Color]::FromArgb(0, 255, 255, 255))
        $g.FillRectangle($sheen, $pad, $pad, $size, ($size * 0.55))
        $g.Clip = $oldClip
        $sheen.Dispose()
    }

    # thin light rim, omitted on tiny sizes where it would blur the silhouette
    if ($S -ge 32) {
        $rim = New-Object Drawing.Pen(
            [Drawing.Color]::FromArgb(70, 255, 255, 255), [Math]::Max(1.0, $S / 64.0))
        $g.DrawPath($rim, $path)
        $rim.Dispose()
    }

    # white J, same proportions as Logo.Draw
    $fontSize = [float]($size * 0.56)
    $font = New-Object Drawing.Font("Segoe UI", $fontSize, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object Drawing.StringFormat
    $sf.Alignment = 'Center'
    $sf.LineAlignment = 'Center'
    $rect = New-Object Drawing.RectangleF $pad, ($pad + $size * 0.02), $size, ($size * 0.92)
    $g.DrawString("J", $font, [Drawing.Brushes]::White, $rect, $sf)

    $font.Dispose(); $sf.Dispose(); $brush.Dispose(); $path.Dispose()

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
