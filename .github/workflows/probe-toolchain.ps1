# Reports what the Windows image offers as a C# compiler, as check-run annotations.
#
# The release build failed with nothing but "exit code 1", and the raw job log is unreachable
# from the environment that maintains this repository, so this script answers the questions the
# log would have answered. GitHub caps annotations per step at about ten, so the findings are
# packed into a few long ones instead of one per fact.
$ErrorActionPreference = "Continue"
$report = New-Object System.Collections.Generic.List[string]
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$src = Join-Path $repoRoot "src"

function Add-Report([string]$line) { $report.Add($line) }

$fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$cscDll = ""
$dir = Join-Path $env:RUNNER_TEMP "probe"

try {
    $ver = (dotnet --version 2>&1 | Out-String).Trim()
    Add-Report ("dotnet: " + $ver)

    $info = (dotnet --info 2>&1 | Out-String)
    if ($info -match 'Base Path:\s*(.+)') {
        $basePath = $Matches[1].Trim()
        $cscDll = Join-Path $basePath "Roslyn\bincore\csc.dll"
        $rsp = Join-Path $basePath "Roslyn\bincore\csc.rsp"
        Add-Report ("base=" + $basePath)
        Add-Report ("csc.dll exists=" + (Test-Path $cscDll) + "; csc.rsp next to it exists=" + (Test-Path $rsp))
    }

    # A default reference file is what supplies mscorlib when a compile passes no /r: switch.
    # Without one, "dotnet csc.dll" cannot even see System.Object.
    $rspRun = Join-Path ([IO.Path]::GetDirectoryName($cscDll)) "csc.rsp"
    if (Test-Path $rspRun) {
        Add-Report ("csc.rsp: " + ((Get-Content $rspRun | Select-Object -First 8) -join " "))
    } else {
        Add-Report "csc.rsp: MISSING - no default references"
    }

    Add-Report ("framework dir exists=" + (Test-Path $fw))
    foreach ($n in @("csc.exe", "mscorlib.dll", "System.dll", "System.Windows.Forms.dll")) {
        $p = Join-Path $fw $n
        $ok = Test-Path $p
        $v = if ($ok) { (Get-Item $p).VersionInfo.FileVersion } else { "-" }
        Add-Report ("fw " + $n + " exists=" + $ok + " v=" + $v)
    }

    New-Item -ItemType Directory -Force $dir | Out-Null
    $src = Join-Path $dir "p.cs"
    'class P { static void Main() { System.Console.WriteLine(1); } }' | Set-Content -Path $src -Encoding ASCII

    if ($cscDll -and (Test-Path $cscDll)) {
        $sets = [ordered]@{
            "none"           = @()
            "mscorlib"       = @((Join-Path $fw "mscorlib.dll"))
            "build-refs"     = @(
                (Join-Path $fw "mscorlib.dll"),
                (Join-Path $fw "System.dll"),
                (Join-Path $fw "System.Core.dll"),
                (Join-Path $fw "System.Drawing.dll"),
                (Join-Path $fw "System.Windows.Forms.dll")
            )
        }
        foreach ($k in $sets.Keys) {
            $out = Join-Path $dir ("p_" + $k + ".exe")
            $a = @("/nologo", "/target:exe", "/langversion:7.3", "/out:$out")
            foreach ($r in $sets[$k]) { $a += "/r:$r" }
            $a += $src
            $text = (& dotnet $cscDll @a 2>&1 | Out-String).Trim()
            $made = Test-Path $out
            $code = $LASTEXITCODE
            if ($text.Length -gt 260) { $text = $text.Substring(0, 260) + "..." }
            Add-Report ("compile[" + $k + "] exit=" + $code + " produced=" + $made + " :: " + ($text -replace "\s+", " "))
        }
    }
} catch {
    Add-Report ("PROBE CRASHED: " + $_)
}

# --- does the application source compile, and does the whole build work? ---------------------
# The same wrapper and switches build.ps1 uses, pointed at the real sources, then the real
# script. Annotations carry the first diagnostics, which is what the unreachable log would have.
$fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$wrapper = Join-Path $env:RUNNER_TEMP "csc-probe.cmd"
if ($cscDll -and (Test-Path $cscDll)) {
    $refs = @("mscorlib.dll", "System.dll", "System.Core.dll", "System.Drawing.dll",
              "System.Windows.Forms.dll", "System.Web.Extensions.dll", "System.Security.dll",
              "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll") |
        ForEach-Object { Join-Path $fw $_ } | Where-Object { Test-Path $_ } |
        ForEach-Object { "/r:`"$_`"" }
    "@echo off`r`ndotnet `"$cscDll`" $($refs -join ' ') %*" | Set-Content -Path $wrapper -Encoding ASCII
}

try {
    if ($wrapper -and (Test-Path $wrapper)) {
        $out = Join-Path $env:RUNNER_TEMP "probe-app.exe"
        $srcs = Get-ChildItem -Path $src -Filter *.cs -Recurse | Where-Object { $_.Name -ne "Setup.cs" } | ForEach-Object { '"' + $_.FullName + '"' }
        $a = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $wrapper, "/nologo", "/target:winexe",
               "/platform:anycpu", "/langversion:7.3", "/out:`"$out`"") + $srcs
        $text = (& powershell @a 2>&1 | Out-String)
        $errs = @($text -split "`r?`n" | Where-Object { $_ -match ": error " })
        Add-Report ("app compile: errors=" + $errs.Count + " produced=" + (Test-Path $out))
        foreach ($e in ($errs | Select-Object -First 6)) { Add-Report ("  " + ($e -replace "\s+", " ")) }
    }

    # The build script itself. This writes into dist\, which is what the workflow does anyway.
    Add-Report "running build.ps1 with the wrapper"
    $btext = (& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot "build\build.ps1") -Csc $wrapper 2>&1 | Out-String)
    $berrs = @($btext -split "`r?`n" | Where-Object { $_.Trim() })
    $main = Join-Path $repoRoot "dist\JoeBrowser.exe"
    $size = 0
    if (Test-Path $main) { $size = (Get-Item $main).Length }
    Add-Report ("build.ps1 exit=" + $LASTEXITCODE + " produced=" + (Test-Path $main) + " size=" + $size)
    if ($LASTEXITCODE -ne 0) {
        foreach ($l in ($berrs | Select-Object -First 6)) { Add-Report ("  " + ($l -replace "\s+", " ")) }
    }
} catch {
    Add-Report ("COMPILE PROBE CRASHED: " + $_)
}

# Emit the report as a few annotations; each holds at most about 1024 characters.
$chunk = ""
foreach ($line in $report) {
    if ($chunk.Length + $line.Length + 3 -gt 900) {
        Write-Host ("::notice title=toolchain::$chunk")
        $chunk = ""
    }
    $chunk = if ($chunk) { $chunk + " || " + $line } else { $line }
}
if ($chunk) { Write-Host ("::notice title=toolchain::$chunk") }
Write-Host "probe done"
