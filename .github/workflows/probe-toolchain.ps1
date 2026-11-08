# Reports what the Windows image offers as a C# compiler, as check-run annotations.
#
# The release build failed with nothing but "exit code 1", and the raw job log is unreachable
# from the environment that maintains this repository, so this script answers the questions the
# log would have answered. GitHub caps annotations per step at about ten, so the findings are
# packed into a few long ones instead of one per fact.
$ErrorActionPreference = "Continue"
$report = New-Object System.Collections.Generic.List[string]

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
