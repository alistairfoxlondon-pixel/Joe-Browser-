# Opens a page and grabs the screen as fast as possible, so a page that
# repaints itself over and over - the About page used to shake between two
# heights for as long as it was open - shows up as a run of different frames.
param(
    [string]$Exe = "C:\dev\joe-browser\dist\JoeBrowser.exe",
    [string]$Dir = "C:\dev\joe-browser\dist\about",
    [int]$Frames = 10,
    [int]$X = 85,
    [int]$Y = 577,
    [int]$Width = 1440,
    [int]$Height = 900
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if (Test-Path $Dir) { Remove-Item $Dir -Recurse -Force }
New-Item -ItemType Directory -Force $Dir | Out-Null

& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "ui.ps1") `
    -Exe $Exe -Out (Join-Path $Dir "start.png") -ClickX $X -ClickY $Y `
    -ResizeW $Width -ResizeH $Height -Screen -NoCapture | Out-Null

$bmp = New-Object System.Drawing.Bitmap($Width, $Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
for ($i = 0; $i -lt $Frames; $i++) {
    $g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size($Width, $Height)))
    $bmp.Save((Join-Path $Dir ("f{0:d2}.png" -f $i)), [System.Drawing.Imaging.ImageFormat]::Png)
}
$g.Dispose(); $bmp.Dispose()
$hashes = Get-ChildItem $Dir -Filter "f*.png" | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
$distinct = ($hashes | Select-Object -Unique).Count
Write-Host ("frames: $Frames, distinct images: $distinct")
if ($distinct -le 2) { Write-Host "STABLE: the page is not repainting itself" }
else { Write-Host "UNSTABLE: the page repaints itself $distinct times" }
