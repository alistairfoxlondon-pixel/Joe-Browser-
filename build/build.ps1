# Builds JoeBrowser.exe (WinForms, .NET Framework 4.8, no external dependencies at runtime)
param(
    [string]$Csc = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"
$fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"

function Find-Csc {
    $candidates = @()
    if ($Csc) { $candidates += $Csc }
    $candidates += (Join-Path $PSScriptRoot "tools\csc.exe")
    $candidates += (Join-Path $env:TEMP "2\opencode\roslyn\tasks\net472\csc.exe")
    $candidates += "C:\Users\Administrator\AppData\Local\Temp\2\opencode\roslyn\tasks\net472\csc.exe"
    $candidates += (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe")

    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    throw "No C# compiler found. Install the .NET Framework 4.8 Developer Pack, or pass -Csc <path to csc.exe>."
}

$cscPath = Find-Csc
$version = (Get-Item $cscPath).VersionInfo.FileVersion
Write-Host "Using compiler: $cscPath ($version)"

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$sources = Get-ChildItem -Path $src -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
if (-not $sources) { throw "No source files found." }

$refNames = @("System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll", "System.Web.Extensions.dll", "System.Security.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll")
$out = Join-Path $dist "JoeBrowser.exe"

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("/nologo")
$lines.Add("/target:winexe")
$lines.Add("/platform:anycpu")
$lines.Add("/langversion:7.3")
$lines.Add("/optimize+")
$lines.Add("/debug-")
$lines.Add('/win32icon:"' + (Join-Path $src "app.ico") + '"')
$lines.Add('/win32manifest:"' + (Join-Path $src "app.manifest") + '"')
$lines.Add('/out:"' + $out + '"')
foreach ($r in $refNames) { $lines.Add('/r:"' + (Join-Path $fw $r) + '"') }
foreach ($s in $sources) { $lines.Add('"' + $s + '"') }

$rsp = Join-Path $PSScriptRoot "build.rsp"
Set-Content -Path $rsp -Value $lines -Encoding ASCII

& $cscPath "@$rsp"
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

Remove-Item $rsp -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $src "JoeBrowser.exe.config") (Join-Path $dist "JoeBrowser.exe.config") -Force
$fontSrc = Join-Path $src "fonts"
if (Test-Path $fontSrc) {
    $fontDst = Join-Path $dist "fonts"
    if (-not (Test-Path $fontDst)) { New-Item -ItemType Directory -Path $fontDst | Out-Null }
    $fontFiles = @(Get-ChildItem (Join-Path $fontSrc "*.ttf")) + @(Get-ChildItem (Join-Path $fontSrc "*.otf"))
    foreach ($f in $fontFiles) {
        $target = Join-Path $fontDst $f.Name
        # A running app registers its fonts from a TEMP copy, but GDI can still hold the source
        # open, so a locked destination font is a warning rather than a failed build.
        try {
            if (-not (Test-Path $target) -or (Get-Item $target).Length -ne $f.Length) {
                Copy-Item $f.FullName $target -Force -ErrorAction Stop
            }
        } catch { Write-Warning ("Font " + $f.Name + " is busy, kept the copy already in dist") }
    }
}
Copy-Item (Join-Path $root "README.md") $dist -Force
Copy-Item (Join-Path $root "LICENSE") $dist -Force

Write-Host ""
Write-Host "Built: $out"
Write-Host ("Size : {0:N0} KB" -f ((Get-Item $out).Length / 1KB))
