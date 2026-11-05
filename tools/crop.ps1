param(
    [Parameter(Mandatory = $true)][string]$In,
    [Parameter(Mandatory = $true)][string]$Out,
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [Parameter(Mandatory = $true)][int]$W,
    [Parameter(Mandatory = $true)][int]$H,
    [int]$Zoom = 1
)
Add-Type -AssemblyName System.Drawing
$src = New-Object System.Drawing.Bitmap($In)
[int]$w = [Math]::Min($W, $src.Width - $X)
[int]$h = [Math]::Min($H, $src.Height - $Y)
$rect = New-Object System.Drawing.Rectangle($X, $Y, $w, $h)
$part = $src.Clone($rect, $src.PixelFormat)
$src.Dispose()
if ($Zoom -le 1) {
    $part.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $part.Dispose()
} else {
    [int]$bw = $w * $Zoom
    [int]$bh = $h * $Zoom
    $big = New-Object System.Drawing.Bitmap($bw, $bh)
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.DrawImage($part, 0, 0, $bw, $bh)
    $g.Dispose()
    $part.Dispose()
    $big.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $big.Dispose()
}
Write-Host ("cropped " + $w + "x" + $h + " from " + $In + " to " + $Out)
