# Walks the sidebar of the running app and captures one screenshot per page.
#
# The capture is taken from the screen, not with PrintWindow: a window that has
# not repainted every region keeps the pixels it had, and PrintWindow hands
# those back as if they were current. The screen shows what a person sees.
param(
    [string]$Exe = "C:\dev\joe-browser\dist\JoeBrowser.exe",
    [string]$Dir = "C:\dev\joe-browser\dist\pages",
    [int]$ResizeW = 1440,
    [int]$ResizeH = 900,
    [int]$Settle = 1200
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Dir)) { New-Item -ItemType Directory -Force $Dir | Out-Null }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$pages = @(
    @{ Name = "profiles"; Y = 174 },
    @{ Name = "history";  Y = 220 },
    @{ Name = "account";  Y = 266 },
    @{ Name = "proxies";  Y = 312 },
    @{ Name = "ext";      Y = 358 },
    @{ Name = "browsers"; Y = 404 },
    @{ Name = "settings"; Y = 485 },
    @{ Name = "tools";    Y = 531 },
    @{ Name = "about";    Y = 577 }
)

foreach ($p in $pages) {
    $out = Join-Path $Dir ($p.Name + ".png")
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "ui.ps1") `
        -Exe $Exe -Out $out -ClickX 95 -ClickY $p.Y -ResizeW $ResizeW -ResizeH $ResizeH `
        -Screen -NoCapture | Out-Null
    # The window is at the top left of the work area, so the screen grab is cropped to it.
    # The pause matters: a grab taken while the window is still repainting shows the
    # pixels of the page it was on a moment ago.
    Start-Sleep -Milliseconds $Settle
    $bmp = New-Object System.Drawing.Bitmap($ResizeW, $ResizeH)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size($ResizeW, $ResizeH)))
    $g.Dispose()
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("captured " + $p.Name)
}
