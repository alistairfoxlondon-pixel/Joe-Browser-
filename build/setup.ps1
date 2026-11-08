# Builds dist\JoeBrowserSetup.exe: the installer, with the application embedded as a payload.
#
# The installer is per-user (installs to %LOCALAPPDATA%\Programs\JoeBrowser), so it never asks
# for administrator rights. That is deliberate: an unelevated installer produces no UAC prompt
# and nothing for Windows Security to treat as an elevated setup.
param(
    [string]$Out = "",
    [string]$PfxPath = "",
    [string]$PfxPassword = "",
    [string]$CertThumbprint = "",
    # Same contract as build.ps1: point the script at any C# compiler, so the installer can be
    # built with the Roslyn that ships in the .NET SDK on a machine with no Developer Pack.
    [string]$Csc = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$fw = Join-Path $env:WINDIR "Microsoft\NET\Framework64\v4.0.30319"
if (-not (Test-Path $fw)) { $fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319" }
$csc = $Csc
if (-not $csc) { $csc = Join-Path $PSScriptRoot "tools\csc.exe" }
if (-not (Test-Path $csc)) {
    throw "No compiler at $csc. Pass -Csc <path to a C# compiler>, or place one at build\tools\csc.exe."
}
if (-not $Out) { $Out = Join-Path $root "dist\JoeBrowserSetup.exe" }

$app = Join-Path $root "dist\JoeBrowser.exe"
if (-not (Test-Path $app)) { throw "Build the application first: build\build.ps1" }

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("/nologo")
$lines.Add("/target:winexe")
$lines.Add("/platform:anycpu")
$lines.Add("/langversion:7.3")
$lines.Add("/optimize+")
$lines.Add("/debug-")
$lines.Add("/main:JoeBrowser.Setup.Program")
$lines.Add('/win32icon:"' + (Join-Path $root "src\app.ico") + '"')
$lines.Add('/win32manifest:"' + (Join-Path $root "src\app.manifest") + '"')
# The application travels inside the installer as a resource.
$lines.Add('/resource:"' + $app + '",JoeBrowser.payload.exe')
$lines.Add('/out:"' + $Out + '"')
foreach ($r in @("System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll")) {
    $lines.Add('/r:"' + (Join-Path $fw $r) + '"')
}
$lines.Add('"' + (Join-Path $root "src\Setup.cs") + '"')
$lines.Add('"' + (Join-Path $root "src\Installation.cs") + '"')

$rsp = Join-Path $PSScriptRoot "setup.rsp"
Set-Content -Path $rsp -Value $lines -Encoding ASCII
& $csc "@$rsp"
if ($LASTEXITCODE -ne 0) { throw "Setup build failed with exit code $LASTEXITCODE" }
Remove-Item $rsp -Force -ErrorAction SilentlyContinue

# Optional Authenticode signing. Without a certificate Windows will still warn on first run;
# signing is the only legitimate way to remove that warning.
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "x64" } | Select-Object -First 1
if ($signtool -and ($PfxPath -or $CertThumbprint)) {
    $targets = @($Out, $app)
    foreach ($t in $targets) {
        if ($PfxPath) {
            & $signtool.FullName sign /fd SHA256 /f $PfxPath /p $PfxPassword /tr http://timestamp.digicert.com /td SHA256 $t
        } else {
            & $signtool.FullName sign /fd SHA256 /sha1 $CertThumbprint /tr http://timestamp.digicert.com /td SHA256 $t
        }
    }
    Write-Host "Signed: $($targets -join ', ')"
} elseif ($PfxPath -or $CertThumbprint) {
    Write-Host "Signing was requested but signtool.exe was not found (install the Windows SDK)."
} else {
    Write-Host "Not signed. Pass -PfxPath or -CertThumbprint to sign, which is what removes SmartScreen warnings."
}

$size = (Get-Item $Out).Length / 1MB
Write-Host ("Built: {0}  ({1:N1} MB)" -f $Out, $size)
