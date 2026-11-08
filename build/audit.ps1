# Builds tools\audit\audit.exe: the live fingerprint audit harness.
param([string]$Out = "")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $PSScriptRoot "tools\csc.exe"
if (-not (Test-Path $csc)) { throw "No compiler at $csc" }
if (-not $Out) { $Out = Join-Path $root "tools\audit\audit.exe" }

$srcs = @(Get-ChildItem (Join-Path $root "src") -Filter *.cs -Recurse |
    Where-Object { $_.Name -ne "Setup.cs" } |
    ForEach-Object { $_.FullName })
$srcs += (Join-Path $root "tools\audit\Audit.cs")
$srcs += (Join-Path $root "tools\audit\Suite.cs")
$srcs += (Join-Path $root "tools\audit\Diag.cs")
$srcs += (Join-Path $root "tools\audit\UiAudit.cs")
$srcs += (Join-Path $root "tools\audit\ProxySuite.cs")
$srcs += (Join-Path $root "tools\audit\KernelSuite.cs")
$srcs += (Join-Path $root "tools\audit\SiteSuite.cs")
$srcs += (Join-Path $root "tools\audit\PaySuite.cs")
$srcs += (Join-Path $root "tools\audit\IconBuilder.cs")

$lines = @("/nologo", "/target:exe", "/platform:anycpu", "/langversion:7.3", "/optimize+", "/main:JoeBrowser.Audit", ('/out:"' + $Out + '"'))
$lines += ('/resource:"' + (Join-Path $root "src\app.ico") + '",JoeBrowser.app.ico')
foreach ($logo in @("chrome", "chromium", "brave", "firefox", "edge", "usdc", "usdt", "dai", "bnb", "sol", "ltc", "matic", "trx")) {
    $lines += ('/resource:"' + (Join-Path $root ("src\logos\" + $logo + ".png")) + '",JoeBrowser.logos.' + $logo + '.png')
}
foreach ($r in @("System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll", "System.Web.Extensions.dll", "System.Security.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll")) {
    $lines += ('/r:"' + (Join-Path $fw $r) + '"')
}
foreach ($s in $srcs) { $lines += ('"' + $s + '"') }

$rsp = Join-Path $PSScriptRoot "audit.rsp"
Set-Content -Path $rsp -Value $lines -Encoding ASCII
& $csc "@$rsp"
if ($LASTEXITCODE -ne 0) { throw "Audit build failed with exit code $LASTEXITCODE" }
Remove-Item $rsp -Force -ErrorAction SilentlyContinue
Write-Host "Built: $Out"
