# Builds JoeBrowser.exe (WinForms, .NET Framework 4.8, no external dependencies at runtime)
param(
    [string]$Csc = "",
    [string]$Configuration = "Release",
    [string]$CertThumbprint = "",
    [string]$PfxPath = "",
    [string]$PfxPassword = ""
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

# Setup.cs has its own entry point and is compiled by build\setup.ps1 into the installer,
# so it must stay out of the application binary.
$sources = Get-ChildItem -Path $src -Filter *.cs -Recurse |
    Where-Object { $_.Name -ne "Setup.cs" } |
    ForEach-Object { $_.FullName }
if (-not $sources) { throw "No source files found." }

$refNames = @("System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll", "System.Configuration.dll", "System.Web.Extensions.dll", "System.Security.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll")
$out = Join-Path $dist "JoeBrowser.exe"

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("/nologo")
$lines.Add("/target:winexe")
$lines.Add("/platform:anycpu")
$lines.Add("/langversion:7.3")
if ($Configuration -eq "Debug") {
    $lines.Add("/optimize-")
    $lines.Add("/debug+")
    $lines.Add("/define:DEBUG;TRACE")
} else {
    $lines.Add("/optimize+")
    $lines.Add("/debug-")
}
$lines.Add('/win32icon:"' + (Join-Path $src "app.ico") + '"')
$lines.Add('/win32manifest:"' + (Join-Path $src "app.manifest") + '"')
# Embed the same artwork as a managed resource so windows, dialogs and the About page can
# load real multi-size icons instead of the generic Windows placeholder.
$lines.Add('/resource:"' + (Join-Path $src "app.ico") + '",JoeBrowser.app.ico')
# Official browser and coin logos, embedded so the app never depends on an installed browser,
# a network fetch or a text placeholder to draw a recognisable icon.
foreach ($logo in @("chrome", "chromium", "brave", "firefox", "edge", "usdc", "usdt", "dai", "bnb", "sol", "ltc", "matic", "trx")) {
    $lines.Add('/resource:"' + (Join-Path $src ("logos\" + $logo + ".png")) + '",JoeBrowser.logos.' + $logo + '.png')
}
# The UI type and the Material icon face travel INSIDE the executable. The installer copies
# only JoeBrowser.exe, so an installed build used to have no fonts folder beside it and drew
# no icons at all; embedding the faces (and extracting them on first run) removes that whole
# class of "the icons are missing" reports. MaterialIcons-Regular.otf is not a valid sfnt and
# is therefore not embedded - it is a stale copy of the .ttf.
$fontDir = Join-Path $src "fonts"
if (Test-Path $fontDir) {
    $fonts = @(Get-ChildItem $fontDir -File | Where-Object { $_.Extension -eq ".ttf" -or $_.Extension -eq ".otf" })
    foreach ($font in $fonts) {
        if ($font.Name -eq "MaterialIcons-Regular.otf") { continue }
        $lines.Add('/resource:"' + $font.FullName + '",JoeBrowser.fonts.' + $font.Name)
    }
}
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

# SmartScreen and antivirus reputation come from a code-signing certificate, not from anything
# the code does. When a certificate is available, pass -CertThumbprint (a cert in the user or
# machine store) or -PfxPath plus -PfxPassword, and the exe is signed as part of the build.
# Without it the build stays unsigned and Windows shows its usual "unknown publisher" warning.
$signTarget = Join-Path $dist "JoeBrowser.exe"
$signArgs = $null
if ($CertThumbprint) {
    $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $CertThumbprint } | Select-Object -First 1
    if (-not $cert) { throw "No certificate with thumbprint $CertThumbprint was found." }
    $signArgs = @("sign", "/sha1", $CertThumbprint, "/fd", "sha256", "/tr", "http://timestamp.digicert.com", "/td", "sha256")
} elseif ($PfxPath) {
    if (-not (Test-Path $PfxPath)) { throw "PFX not found: $PfxPath" }
    $signArgs = @("sign", "/f", $PfxPath, "/fd", "sha256", "/tr", "http://timestamp.digicert.com", "/td", "sha256")
    if ($PfxPassword) { $signArgs += @("/p", $PfxPassword) }
}
if ($signArgs) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        $kit = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
        if (Test-Path $kit) {
            $signtool = Get-ChildItem $kit -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch "arm" } | Sort-Object FullName -Descending | Select-Object -First 1
        }
    }
    if ($signtool) {
        $tool = if ($signtool.Source) { $signtool.Source } else { $signtool.Path }
        & $tool @signArgs $signTarget
        if ($LASTEXITCODE -ne 0) { throw "Signing failed with exit code $LASTEXITCODE" }
        Write-Host "Signed: $signTarget"
    } else {
        Write-Warning "signtool.exe was not found; the exe was built unsigned."
    }
}

Write-Host ""
Write-Host "Built: $out"
Write-Host ("Size : {0:N0} KB" -f ((Get-Item $out).Length / 1KB))
