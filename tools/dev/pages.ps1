# Walks the sidebar of the running app and captures one screenshot per page.
param(
    [string]$Exe = "C:\dev\joe-browser\dist\JoeBrowser.exe",
    [string]$Dir = "C:\dev\joe-browser\dist\pages",
    [int]$ResizeW = 1440,
    [int]$ResizeH = 900
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $Dir)) { New-Item -ItemType Directory -Force $Dir | Out-Null }

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
        -Exe $Exe -Out $out -ClickX 95 -ClickY $p.Y -ResizeW $ResizeW -ResizeH $ResizeH | Out-Null
    Write-Host ("captured " + $p.Name)
}
